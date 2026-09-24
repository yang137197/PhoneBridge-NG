package org.phonebridge.ng

import android.os.SystemClock
import android.util.Base64
import android.util.JsonReader
import android.util.JsonToken
import org.phonebridge.credentials.PairingStore
import org.phonebridge.credentials.StoreError
import org.phonebridge.credentials.StoreException
import org.phonebridge.credentials.StoreSnapshot
import org.phonebridge.pairing.PairingFrames
import org.phonebridge.pairing.PairingSession
import java.io.StringReader
import java.net.ServerSocket
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit

internal class ApiFailure(val status: Int, val code: String) : Exception(code)
internal object CredentialEncoding {
    fun hex(bytes: ByteArray) = bytes.joinToString("") { "%02x".format(it.toInt() and 255) }
    fun encode(bytes: ByteArray): String = Base64.encodeToString(bytes, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
    fun decode(value: String): ByteArray {
        if (!value.matches(Regex("[A-Za-z0-9_-]{43}"))) throw ApiFailure(401, "unauthorized")
        val result = try { Base64.decode(value, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING) } catch (_: Exception) { throw ApiFailure(401, "unauthorized") }
        if (result.size != 32 || encode(result) != value) { result.fill(0); throw ApiFailure(401, "unauthorized") }
        return result
    }
    fun hash(bytes: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(bytes)
    fun validateName(name: String) {
        org.phonebridge.credentials.Rules.name(name) // Same source module, one canonical validator.
    }
    fun submitBody(body: ByteArray): Triple<String, String, ByteArray> {
        try {
            require(body.size in 1..2048)
            val text = Charsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(body)).toString()
            val fields = mutableMapOf<String, String>()
            JsonReader(StringReader(text)).use { reader ->
                reader.isLenient = false; reader.beginObject()
                while (reader.hasNext()) {
                    val name = reader.nextName()
                    require(name in setOf("client_id", "client_name", "credential") && name !in fields)
                    require(reader.peek() == JsonToken.STRING); fields[name] = reader.nextString()
                }
                reader.endObject(); require(reader.peek() == JsonToken.END_DOCUMENT)
            }
            require(fields.size == 3)
            val client = fields.getValue("client_id"); require(client.matches(Regex("[0-9a-f]{32}")))
            val name = fields.getValue("client_name"); validateName(name)
            return Triple(client, name, decode(fields.getValue("credential")))
        } catch (_: Exception) { throw ApiFailure(400, "invalid_request") }
    }
    fun deletePathBody(body: ByteArray): String {
        try {
            require(body.size in 1..2048)
            val text = Charsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(body)).toString()
            var path: String? = null
            JsonReader(StringReader(text)).use { reader ->
                reader.isLenient = false; reader.beginObject()
                while (reader.hasNext()) {
                    require(reader.nextName() == "path" && path == null && reader.peek() == JsonToken.STRING)
                    path = reader.nextString()
                }
                reader.endObject(); require(reader.peek() == JsonToken.END_DOCUMENT)
            }
            return requireNotNull(path)
        } catch (_: Exception) { throw ApiFailure(400, "invalid_request") }
    }
}

internal data class PairingView(val windowId: String, val code: String?, val port: Int,
    val remainingSeconds: Int, val attemptsUsed: Int, val attemptId: String?, val pendingName: String?, val state: String?)

/** Only a foreground local caller can open a window or approve an exact pending attempt. */
internal class PairingController(private val store: PairingStore, private val ca: ByteArray, private val httpsPort: Int,
    private val onAdvertisement: (String?, Int) -> Unit, private val onStoreChanged: (StoreSnapshot) -> Unit,
    private val onFatalStorage: () -> Unit) : AutoCloseable {
    private val gate = Any()
    private val worker = Executors.newSingleThreadExecutor()
    private val timer = Executors.newSingleThreadScheduledExecutor()
    private var window: Window? = null
    private var lastOpened = -10_000L
    private var closed = false
    private class Window(val id: ByteArray, val code: CharArray, val expires: Long, val listener: ServerSocket) {
        var used = 0
        var socket: Socket? = null
        var expiry: ScheduledFuture<*>? = null
        var grantHash: ByteArray? = null
        var attempt: String? = null
        var client: String? = null
        var name: String? = null
        var token: ByteArray? = null
        var tokenHash: ByteArray? = null
        var state: String? = null
        var lastPoll = -1_000L
    }
    fun openWindow(allowed: () -> Boolean = { true }): PairingView = synchronized(gate) {
        val now = SystemClock.elapsedRealtime()
        if (!allowed() || closed || window != null || now - lastOpened < 10_000) throw ApiFailure(409, "conflict")
        store.snapshot() // Storage must be healthy before accepting an attempt.
        val random = SecureRandom(); val code = CharArray(8) { ('0'.code + random.nextInt(10)).toChar() }
        val listener = ServerSocket(0, 1)
        val current = Window(ByteArray(16).also { random.nextBytes(it) }, code, now + 120_000, listener)
        window = current; lastOpened = now
        current.expiry = timer.schedule({ synchronized(gate) { if (window === current) invalidateLocked() } }, 120, TimeUnit.SECONDS)
        onAdvertisement(CredentialEncoding.hex(current.id), listener.localPort)
        worker.execute { accept(current) }
        viewLocked(current)
    }
    fun view(): PairingView? = synchronized(gate) { liveLocked()?.let { viewLocked(it) } }
    private fun viewLocked(w: Window) = PairingView(CredentialEncoding.hex(w.id), if (w.grantHash == null) String(w.code) else null,
        if (w.listener.isClosed) 0 else w.listener.localPort, maxOf(0, ((w.expires - SystemClock.elapsedRealtime()) / 1000).toInt()),
        w.used, w.attempt, if (w.state == "PendingApproval") w.name else null, w.state)
    private fun liveLocked(): Window? {
        if (window?.let { SystemClock.elapsedRealtime() >= it.expires } == true) invalidateLocked()
        return window
    }
    fun cancelWindow() = synchronized(gate) { invalidateLocked() }
    private fun invalidateLocked() {
        val old = window ?: return; window = null
        old.expiry?.cancel(false); old.code.fill('\u0000'); old.grantHash?.fill(0); old.token?.fill(0); old.tokenHash?.fill(0)
        try { old.listener.close() } catch (_: Exception) { }
        try { old.socket?.close() } catch (_: Exception) { }
        onAdvertisement(null, 0)
    }
    private fun accept(w: Window) {
        while (true) {
            val socket = try { w.listener.accept() } catch (_: Exception) { return }
            val code = synchronized(gate) {
                if (liveLocked() !== w || w.used >= 5 || w.grantHash != null) { socket.close(); return }
                w.used++; w.socket = socket; w.code.copyOf()
            }
            try {
                val deadline = minOf(w.expires, SystemClock.elapsedRealtime() + 30_000)
                val total = timer.schedule({ try { socket.close() } catch (_: Exception) { } }, maxOf(1, deadline - SystemClock.elapsedRealtime()), TimeUnit.MILLISECONDS)
                try {
                    PairingSession.createAndroid(w.id, code, httpsPort, ca).use { session ->
                        for (type in listOf(1, 0x11, 0x21, 0x31)) {
                            framed(socket, deadline) { session.acceptFrame(readFrame(socket, type)) }
                            if (type == 0x31) framed(socket, deadline) { if (socket.getInputStream().read() != -1) throw ApiFailure(400, "invalid_request") }
                            framed(socket, deadline) { socket.getOutputStream().write(session.createNextFrame()); socket.getOutputStream().flush() }
                        }
                        session.takeConfirmation().use { confirmation ->
                            val grant = confirmation.copyGrant()
                            try { synchronized(gate) {
                                if (liveLocked() !== w || SystemClock.elapsedRealtime() >= deadline) throw ApiFailure(401, "unauthorized")
                                w.grantHash = CredentialEncoding.hash(grant)
                                w.attempt = CredentialEncoding.hex(confirmation.attemptId); w.client = CredentialEncoding.hex(confirmation.clientId)
                                w.code.fill('\u0000'); w.listener.close(); onAdvertisement(null, 0)
                            } } finally { grant.fill(0) }
                        }
                    }
                } finally { total.cancel(false) }
            } catch (_: Exception) { /* Fixed state only; no peer input or key material is logged. */ }
            finally {
                code.fill('\u0000'); try { socket.close() } catch (_: Exception) { }
                synchronized(gate) {
                    if (window === w) {
                        w.socket = null
                        if (w.grantHash == null && w.used >= 5) invalidateLocked()
                    }
                }
            }
            synchronized(gate) { if (window !== w || w.grantHash != null) return }
        }
    }
    private fun <T> framed(socket: Socket, totalDeadline: Long, action: () -> T): T {
        val duration = minOf(5_000, totalDeadline - SystemClock.elapsedRealtime()); if (duration <= 0) throw ApiFailure(401, "unauthorized")
        socket.soTimeout = duration.toInt()
        val expiry = timer.schedule({ try { socket.close() } catch (_: Exception) { } }, duration, TimeUnit.MILLISECONDS)
        return try { action() } finally { expiry.cancel(false) }
    }
    private fun readFrame(socket: Socket, type: Int): ByteArray {
        fun exact(length: Int): ByteArray {
            val bytes = ByteArray(length); var offset = 0
            while (offset < length) { val n = socket.getInputStream().read(bytes, offset, length - offset); if (n < 1) throw java.io.EOFException(); offset += n }
            return bytes
        }
        val header = exact(PairingFrames.HEADER_SIZE)
        return header + exact(PairingFrames.length(header, type))
    }
    private fun authorizedLocked(attempt: String, grant: ByteArray): Window {
        val w = liveLocked()
        val hash = CredentialEncoding.hash(grant)
        val matches = MessageDigest.isEqual(w?.grantHash ?: ByteArray(32), hash); hash.fill(0)
        if (w == null || !matches || w.attempt != attempt) throw ApiFailure(401, "unauthorized")
        return w
    }
    fun authorize(attempt: String, grant: ByteArray) = synchronized(gate) { authorizedLocked(attempt, grant); Unit }
    fun clientId(attempt: String, grant: ByteArray): String = synchronized(gate) { authorizedLocked(attempt, grant).client!! }
    fun submit(attempt: String, grant: ByteArray, body: ByteArray): PairingView = synchronized(gate) {
        val w = authorizedLocked(attempt, grant)
        val (client, name, token) = CredentialEncoding.submitBody(body)
        try {
            if (w.client != client) throw ApiFailure(409, "conflict")
            val hash = CredentialEncoding.hash(token)
            if (w.tokenHash != null) {
                if (w.name != name || !MessageDigest.isEqual(w.tokenHash, hash)) throw ApiFailure(409, "conflict")
            } else {
                if (w.state != null) throw ApiFailure(409, "conflict")
                w.name = name; w.tokenHash = hash; w.token = token.copyOf(); w.state = "PendingApproval"
            }
            stateAllowed(w); viewLocked(w)
        } finally { token.fill(0) }
    }
    fun poll(attempt: String, grant: ByteArray): PairingView = synchronized(gate) {
        val w = authorizedLocked(attempt, grant); val now = SystemClock.elapsedRealtime()
        if (now - w.lastPoll < 1_000) throw ApiFailure(429, "capacity")
        w.lastPoll = now; stateAllowed(w); viewLocked(w)
    }
    fun cancelAttempt(attempt: String, grant: ByteArray): PairingView = synchronized(gate) {
        val w = authorizedLocked(attempt, grant)
        if (w.state == "Active") throw ApiFailure(409, "conflict")
        if (w.state != "Rejected") w.state = "Cancelled"
        w.token?.fill(0); w.token = null
        stateAllowed(w); viewLocked(w)
    }
    private fun stateAllowed(w: Window) {
        when (w.state) {
            null -> throw ApiFailure(409, "conflict")
            "Rejected" -> throw ApiFailure(403, "rejected")
            "Cancelled" -> throw ApiFailure(410, "cancelled")
        }
    }
    fun decide(attempt: String, approve: Boolean) = synchronized(gate) {
        val w = liveLocked() ?: throw ApiFailure(409, "conflict")
        if (w.attempt != attempt || w.state != "PendingApproval") throw ApiFailure(409, "conflict")
        if (approve) {
            try {
                val snapshot = store.approve(w.client!!, w.name!!, w.token!!, store.snapshot().revision)
                w.state = "Active"; onStoreChanged(snapshot)
            } catch (e: StoreException) {
                if (e.error == StoreError.CAPACITY) throw ApiFailure(429, "capacity")
                if (e.error == StoreError.ALREADY_EXISTS || e.error == StoreError.REVISION_CONFLICT) throw ApiFailure(409, "conflict")
                onFatalStorage(); invalidateLocked(); throw ApiFailure(503, "storage_failure")
            }
        } else w.state = "Rejected"
        w.token?.fill(0); w.token = null
    }
    override fun close() {
        synchronized(gate) { closed = true; invalidateLocked() }
        worker.shutdownNow(); timer.shutdownNow()
    }
}
