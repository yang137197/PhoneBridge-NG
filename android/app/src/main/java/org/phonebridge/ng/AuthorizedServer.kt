package org.phonebridge.ng

import android.util.Base64
import android.os.Build
import android.os.ParcelFileDescriptor
import android.os.SystemClock
import android.system.Os
import android.system.OsConstants
import com.phonebridge.server.HttpByteRange
import com.phonebridge.server.RequestBody
import com.phonebridge.server.SharedPath
import com.phonebridge.server.SharedStorage
import com.phonebridge.server.DeleteSnapshot
import com.phonebridge.server.StorageFailure
import com.phonebridge.server.XmlResponseBuilder
import fi.iki.elonen.NanoHTTPD
import org.json.JSONObject
import org.phonebridge.credentials.AccessMode
import org.phonebridge.credentials.PairedClient
import org.phonebridge.credentials.PairingStore
import org.phonebridge.credentials.StoreError
import org.phonebridge.credentials.StoreException
import org.phonebridge.credentials.StoreSnapshot
import java.io.ByteArrayInputStream
import java.io.File
import java.io.InputStream
import java.net.Socket
import java.security.SecureRandom
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.SynchronousQueue
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit

/** Interrupt transport before Conscrypt tries to drain close_notify into a blocked peer. */
private fun abortSocket(socket: Socket) {
    if (Build.VERSION.SDK_INT >= 29) {
        // fromSocket owns a dup only from API 29; closing it cannot take ownership of the original fd.
        try { ParcelFileDescriptor.fromSocket(socket).use { Os.shutdown(it.fileDescriptor, OsConstants.SHUT_RDWR) } }
        catch (_: Exception) { }
    }
    try { socket.close() } catch (_: Exception) { }
}

internal const val MAX_UPLOAD_BYTES = 20_000_000_000L

internal fun validatedBodyLength(headers: Map<String, String>, maximum: Long): Long {
    if (headers.containsKey("transfer-encoding") || headers.containsKey("content-encoding") || headers.containsKey("expect")) {
        throw ApiFailure(400, "invalid_request")
    }
    val raw = headers["content-length"] ?: return 0
    if (!raw.matches(Regex("0|[1-9][0-9]{0,15}"))) throw ApiFailure(400, "invalid_request")
    return (raw.toLongOrNull() ?: throw ApiFailure(400, "invalid_request")).also {
        if (it > maximum) throw ApiFailure(400, "invalid_request")
    }
}

/** Revocation and registering a newly authorized request use the same gate. */
internal class ClientConnections(private val store: PairingStore, private val onChanged: (StoreSnapshot) -> Unit,
    private val onFatal: () -> Unit) : AutoCloseable {
    private val gate = Any()
    private val sockets = mutableMapOf<Socket, String>()
    private val blocked = mutableSetOf<String>()
    @Volatile var healthy = true
        private set
    fun authenticate(socket: Socket, client: String, token: ByteArray): PairedClient = synchronized(gate) {
        if (!healthy) throw ApiFailure(503, "storage_failure")
        if (client in blocked) throw ApiFailure(401, "unauthorized")
        try { store.authenticate(client, token).also { sockets[socket] = client } }
        catch (e: StoreException) {
            if (e.error == StoreError.UNAUTHORIZED || e.error == StoreError.INVALID_INPUT) throw ApiFailure(401, "unauthorized")
            failLocked(); throw ApiFailure(503, "storage_failure")
        }
    }
    fun release(socket: Socket) = synchronized(gate) { sockets.remove(socket); Unit }
    fun <T> commit(socket: Socket, client: PairedClient, action: () -> T): T = synchronized(gate) {
        if (!healthy) throw ApiFailure(503, "storage_failure")
        if (client.clientId in blocked || sockets[socket] != client.clientId) throw ApiFailure(401, "unauthorized")
        action()
    }
    fun revoke(client: String, except: Socket? = null) = synchronized(gate) {
        if (!healthy) throw ApiFailure(503, "storage_failure")
        blocked.add(client)
        try {
            val result = store.revoke(client, store.snapshot().revision)
            for ((socket, owner) in sockets.toMap()) if (owner == client && socket !== except) closeSocket(socket)
            onChanged(result)
        } catch (_: StoreException) { failLocked(); throw ApiFailure(503, "storage_failure") }
    }
    fun updateMode(client: String, mode: AccessMode) = synchronized(gate) {
        if (!healthy) throw ApiFailure(503, "storage_failure")
        try {
            val result = store.updateMode(client, mode, store.snapshot().revision)
            for ((socket, owner) in sockets.toMap()) if (owner == client) closeSocket(socket)
            onChanged(result)
        } catch (_: StoreException) { failLocked(); throw ApiFailure(503, "storage_failure") }
    }
    private fun failLocked() {
        healthy = false
        for (socket in sockets.keys.toList()) closeSocket(socket)
        onFatal()
    }
    private fun closeSocket(socket: Socket) = abortSocket(socket)
    override fun close() = synchronized(gate) { healthy = false; sockets.keys.toList().forEach(::closeSocket); sockets.clear() }
}

internal class AuthorizedServer(port: Int, root: File, private val fingerprint: String,
    private val pairing: () -> PairingController, private val connections: ClientConnections,
    private val deletionLifetimeMillis: Long = 30_000,
    private val clock: () -> Long = { SystemClock.elapsedRealtime() }) : NanoHTTPD(port) {
    private val storage = SharedStorage(root)
    private val current = ThreadLocal<Socket>()
    private val clients = ConcurrentHashMap.newKeySet<ClientHandler>()
    private val executor = ThreadPoolExecutor(0, 8, 30, TimeUnit.SECONDS, SynchronousQueue())
    private val timeouts = Executors.newSingleThreadScheduledExecutor()
    private val deadline = ThreadLocal<ScheduledFuture<*>>()
    private data class PendingDelete(val clientId: String, val expiresAt: Long, val snapshot: DeleteSnapshot)
    private val deleteGate = Any()
    private val deletions = LinkedHashMap<String, PendingDelete>()
    private val random = SecureRandom()
    init {
        setAsyncRunner(object : AsyncRunner {
            override fun exec(code: ClientHandler) {
                clients.add(code)
                try { executor.execute(code) } catch (_: java.util.concurrent.RejectedExecutionException) { clients.remove(code); code.close() }
            }
            override fun closed(clientHandler: ClientHandler) { clients.remove(clientHandler) }
            override fun closeAll() { clients.toList().forEach { it.close() }; executor.shutdownNow() }
        })
    }
    override fun createClientHandler(socket: Socket, input: InputStream): ClientHandler = object : ClientHandler(input, socket) {
        override fun run() {
            current.set(socket)
            try {
                deadline.set(timeouts.schedule({ abortSocket(socket) }, 5, TimeUnit.SECONDS))
                super.run()
            } finally { deadline.get()?.cancel(false); deadline.remove(); connections.release(socket); current.remove() }
        }
        override fun close() { try { abortSocket(socket) } finally { super.close() } }
    }
    override fun stop() { try { super.stop() } finally {
        synchronized(deleteGate) { deletions.clear() }
        storage.close(); executor.shutdownNow(); timeouts.shutdownNow()
    } }
    override fun useGzipWhenAccepted(response: Response) = false
    override fun serve(session: IHTTPSession): Response {
        try {
            if (!connections.healthy) throw ApiFailure(503, "storage_failure")
            val path = session.uri; val method = session.method.name
            val attempt = Regex("/phonebridge/v1/pairing/([0-9a-f]{32})").matchEntire(path)?.groupValues?.get(1)
            if (attempt != null) {
                val header = session.headers["authorization"] ?: throw ApiFailure(401, "unauthorized")
                if (!header.startsWith("Bearer ") || header.length > 512) throw ApiFailure(401, "unauthorized")
                val grant = CredentialEncoding.decode(header.substring(7))
                try {
                    val controller = pairing(); controller.authorize(attempt, grant)
                    val length = bodyLength(session, 2048)
                    val view = when (method) {
                        "POST" -> {
                            if (session.headers["content-type"]?.lowercase() !in setOf("application/json", "application/json; charset=utf-8")) throw ApiFailure(400, "invalid_request")
                            val body = readBody(session, length.toInt())
                            try { controller.submit(attempt, grant, body) } finally { body.fill(0) }
                        }
                        "GET" -> { requireEmpty(length); controller.poll(attempt, grant) }
                        "DELETE" -> { requireEmpty(length); controller.cancelAttempt(attempt, grant) }
                        else -> throw ApiFailure(405, "method_not_allowed")
                    }
                    return json(if (view.state == "Active") 200 else 202, JSONObject().put("attempt_id", attempt)
                        .put("client_id", sessionClient(controller, attempt, grant)).put("state", view.state))
                } finally { grant.fill(0) }
            }
            val socket = current.get() ?: throw ApiFailure(503, "storage_failure")
            val client = authenticate(session, socket)
            val length = bodyLength(session, if (method == "PUT") MAX_UPLOAD_BYTES else 1_048_576)
            if (path == "/phonebridge/v1/session") {
                requireEmpty(length); if (method != "GET") throw ApiFailure(405, "method_not_allowed")
                val mode = when (client.mode) { AccessMode.READ_ONLY -> "readOnly"; AccessMode.SAFE -> "safe"; AccessMode.READ_WRITE -> "readWrite" }
                return json(200, JSONObject().put("device_id", "pbng-$fingerprint").put("client_id", client.clientId).put("mode", mode).put("share_ready", true))
            }
            if (path == "/phonebridge/v1/pairings/self") {
                requireEmpty(length); if (method != "DELETE") throw ApiFailure(405, "method_not_allowed")
                connections.revoke(client.clientId, socket)
                return response(204, "application/json", "")
            }
            if (path == "/phonebridge/v1/deletions") {
                if (method != "POST") throw ApiFailure(405, "method_not_allowed")
                if (client.mode == AccessMode.READ_ONLY) throw ApiFailure(403, "rejected")
                if (session.headers["content-type"]?.lowercase() !in setOf("application/json", "application/json; charset=utf-8")) {
                    throw ApiFailure(400, "invalid_request")
                }
                val body = readBody(session, length.toInt())
                val target = try { parseDeletePath(body) } finally { body.fill(0) }
                val snapshot = storage.prepareDelete(target)
                val id = newDeletion(client.clientId, snapshot)
                return json(200, JSONObject().put("confirmation_id", id).put("path", snapshot.entry.path.path)
                    .put("directory", snapshot.entry.directory).put("size", snapshot.entry.size)
                    .put("modified", snapshot.entry.modified).put("expires_in_seconds", (deletionLifetimeMillis + 999) / 1000))
            }
            val deletionId = Regex("/phonebridge/v1/deletions/([0-9a-f]{32})").matchEntire(path)?.groupValues?.get(1)
            if (deletionId != null) {
                requireEmpty(length)
                if (method != "DELETE") throw ApiFailure(405, "method_not_allowed")
                val pending = consumeDeletion(deletionId, client.clientId)
                if (client.mode == AccessMode.READ_ONLY) throw ApiFailure(403, "rejected")
                storage.deleteConfirmed(pending.snapshot) { action -> connections.commit(socket, client, action) }
                return response(204, "application/json", "")
            }
            if (path == "/phonebridge" || path.startsWith("/phonebridge/")) throw ApiFailure(404, "not_found")
            return files(method, session, client, socket, length)
        } catch (e: ApiFailure) { return json(e.status, JSONObject().put("code", e.code)) }
        catch (e: StorageFailure) { return json(e.status, JSONObject().put("code", when (e.status) {
            403 -> "rejected"; 404 -> "not_found"; 405 -> "method_not_allowed"; 409, 412 -> "conflict"; else -> "invalid_request"
        })) }
        catch (_: Exception) { return json(400, JSONObject().put("code", "invalid_request")) }
        finally { deadline.get()?.cancel(false) }
    }
    private fun sessionClient(controller: PairingController, attempt: String, grant: ByteArray): String = controller.clientId(attempt, grant)
    private fun authenticate(session: IHTTPSession, socket: Socket): PairedClient {
        val header = session.headers["authorization"] ?: throw ApiFailure(401, "unauthorized")
        if (!header.startsWith("Basic ") || header.length > 512) throw ApiFailure(401, "unauthorized")
        val encoded = header.substring(6)
        val raw = try { Base64.decode(encoded, Base64.NO_WRAP) } catch (_: Exception) { throw ApiFailure(401, "unauthorized") }
        try {
            if (Base64.encodeToString(raw, Base64.NO_WRAP) != encoded) throw ApiFailure(401, "unauthorized")
            val value = raw.toString(Charsets.US_ASCII)
            val match = Regex("pbng-([0-9a-f]{32}):([A-Za-z0-9_-]{43})").matchEntire(value) ?: throw ApiFailure(401, "unauthorized")
            val token = CredentialEncoding.decode(match.groupValues[2])
            try { return connections.authenticate(socket, match.groupValues[1], token) } finally { token.fill(0) }
        } finally { raw.fill(0) }
    }
    private fun bodyLength(session: IHTTPSession, maximum: Long) = validatedBodyLength(session.headers, maximum)
    private fun readBody(session: IHTTPSession, length: Int): ByteArray {
        val body = ByteArray(length); var offset = 0; val deadline = System.nanoTime() + 5_000_000_000L
        while (offset < length) {
            if (System.nanoTime() > deadline) throw ApiFailure(400, "invalid_request")
            val count = session.inputStream.read(body, offset, length - offset)
            if (count <= 0) throw ApiFailure(400, "invalid_request"); offset += count
        }
        return body
    }
    private fun requireEmpty(length: Long) { if (length != 0L) throw ApiFailure(400, "invalid_request") }
    private fun parseDeletePath(body: ByteArray): SharedPath {
        return SharedPath.parse(CredentialEncoding.deletePathBody(body))
    }
    private fun newDeletion(clientId: String, snapshot: DeleteSnapshot): String = synchronized(deleteGate) {
        val now = clock()
        deletions.entries.removeAll { it.value.expiresAt <= now }
        if (deletions.size >= 16) throw ApiFailure(429, "capacity")
        var id: String
        do { id = CredentialEncoding.hex(ByteArray(16).also { random.nextBytes(it) }) } while (deletions.containsKey(id))
        deletions[id] = PendingDelete(clientId, now + deletionLifetimeMillis, snapshot)
        id
    }
    private fun consumeDeletion(id: String, clientId: String): PendingDelete = synchronized(deleteGate) {
        val pending = deletions.remove(id) ?: throw ApiFailure(404, "not_found")
        if (pending.clientId != clientId) throw ApiFailure(403, "rejected")
        if (pending.expiresAt <= clock()) throw ApiFailure(409, "conflict")
        pending
    }
    private fun extendDeadline(seconds: Long) {
        val socket = current.get() ?: throw ApiFailure(503, "storage_failure")
        deadline.get()?.cancel(false)
        deadline.set(timeouts.schedule({ abortSocket(socket) }, seconds, TimeUnit.SECONDS))
    }
    private fun files(method: String, session: IHTTPSession, client: PairedClient, socket: Socket, length: Long): Response {
        val writable = client.mode != AccessMode.READ_ONLY
        val allow = if (!writable) "OPTIONS, PROPFIND, GET, HEAD" else if (client.mode == AccessMode.SAFE)
            "OPTIONS, PROPFIND, GET, HEAD, PUT, MKCOL, MOVE, COPY" else
            "OPTIONS, PROPFIND, GET, HEAD, PUT, MKCOL, MOVE, COPY, DELETE"
        if (method == "OPTIONS") { requireEmpty(length); return response(200, "text/plain", "").also { it.addHeader("DAV", "1"); it.addHeader("Allow", allow) } }
        if (method in setOf("PUT", "MKCOL", "MOVE", "COPY", "DELETE") && !writable) throw ApiFailure(403, "rejected")
        if (method !in setOf("PROPFIND", "GET", "HEAD", "PUT", "MKCOL", "MOVE", "COPY", "DELETE")) throw ApiFailure(405, "method_not_allowed")
        val path = SharedPath.parse(session.uri)
        val commit = { action: () -> Unit -> connections.commit(socket, client, action) }
        when (method) {
            "PUT" -> {
                if (!session.headers.containsKey("content-length")) throw ApiFailure(400, "invalid_request")
                extendDeadline(120)
                val refreshInterval = TimeUnit.SECONDS.toNanos(30)
                var nextRefresh = System.nanoTime() + refreshInterval
                val created = storage.put(path, session.inputStream, length, client.mode == AccessMode.READ_WRITE,
                    received = {
                        val now = System.nanoTime()
                        if (now >= nextRefresh) {
                            extendDeadline(120)
                            nextRefresh = now + refreshInterval
                        }
                    }, commit = commit)
                return response(if (created) 201 else 204, "application/json", "")
            }
            "MKCOL" -> {
                requireEmpty(length); storage.mkdir(path, commit)
                return response(201, "application/json", "")
            }
            "MOVE", "COPY" -> {
                requireEmpty(length)
                val destination = SharedPath.destination(session.headers["destination"], session.headers["host"])
                val overwriteHeader = session.headers["overwrite"] ?: "T"
                if (overwriteHeader !in setOf("T", "F")) throw ApiFailure(400, "invalid_request")
                val overwrite = client.mode == AccessMode.READ_WRITE && overwriteHeader == "T"
                val created = if (method == "MOVE") storage.move(path, destination, overwrite, commit)
                    else storage.copy(path, destination, overwrite, commit)
                return response(if (created) 201 else 204, "application/json", "")
            }
            "DELETE" -> {
                requireEmpty(length)
                if (client.mode != AccessMode.READ_WRITE) throw ApiFailure(403, "rejected")
                storage.delete(path, commit)
                return response(204, "application/json", "")
            }
        }
        if (method != "PROPFIND") requireEmpty(length)
        else RequestBody.discard(session.inputStream, length, System.nanoTime() + 5_000_000_000L)
        val info = storage.info(path)
        if (method == "PROPFIND") {
            val depth = session.headers["depth"] ?: "1"
            if (depth !in setOf("0", "1")) throw ApiFailure(400, "invalid_request")
            val entries = listOf(info) + if (info.directory && depth == "1") storage.list(path).filter { !it.path.isRoot && it.path.parts.firstOrNull() != "phonebridge" } else emptyList()
            return response(207, "application/xml; charset=utf-8", XmlResponseBuilder.buildPropfindResponse(entries)).also { it.addHeader("DAV", "1") }
        }
        if (method == "HEAD") return response(200, if (info.directory) "httpd/unix-directory" else XmlResponseBuilder.guessMimeType(path.name), "")
            .also { if (!info.directory) it.addHeader("Content-Length", info.size.toString()) }
        if (info.directory) throw ApiFailure(405, "method_not_allowed")
        val stream = storage.read(path)
        try {
            val size = stream.channel.size()
            val range = HttpByteRange.parse(if (session.headers.containsKey("if-range")) null else session.headers["range"], size)
            if (range == HttpByteRange.Unsatisfiable) {
                stream.close(); return response(416, "text/plain", "").also { it.addHeader("Content-Range", "bytes */$size"); it.addHeader("Accept-Ranges", "bytes") }
            }
            val partial = range as? HttpByteRange.Partial
            if (partial != null) stream.channel.position(partial.first)
            return headers(newFixedLengthResponse(status(if (partial == null) 200 else 206), XmlResponseBuilder.guessMimeType(path.name), stream, partial?.length ?: size))
                .also { it.addHeader("Accept-Ranges", "bytes"); if (partial != null) it.addHeader("Content-Range", "bytes ${partial.first}-${partial.last}/$size") }
        } catch (e: Exception) { stream.close(); throw e }
    }
    private fun status(code: Int): Response.IStatus = object : Response.IStatus {
        override fun getRequestStatus() = code
        override fun getDescription() = "$code ${Response.Status.lookup(code)?.description?.substringAfter(' ') ?: "Response"}"
    }
    private fun headers(response: Response): Response = response.also { it.addHeader("Cache-Control", "no-store"); it.addHeader("X-Content-Type-Options", "nosniff"); it.closeConnection(true) }
    private fun response(code: Int, mime: String, text: String): Response {
        val bytes = text.toByteArray(Charsets.UTF_8)
        return headers(newFixedLengthResponse(status(code), mime, ByteArrayInputStream(bytes), bytes.size.toLong()))
    }
    private fun json(code: Int, body: JSONObject): Response {
        val text = body.toString(); check(text.toByteArray(Charsets.UTF_8).size <= 4096)
        return response(code, "application/json; charset=utf-8", text)
    }
}
