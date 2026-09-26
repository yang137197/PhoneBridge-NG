package org.phonebridge.ng

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import android.provider.Settings
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.IBinder
import com.phonebridge.server.TlsHelper
import com.phonebridge.server.TlsIdentityStore
import com.phonebridge.server.SharedPath
import com.phonebridge.server.SharedStorage
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.phonebridge.credentials.ClientState
import org.phonebridge.credentials.CommitStage
import org.phonebridge.credentials.AccessMode
import org.phonebridge.credentials.PairingStore
import org.phonebridge.pairing.PairingFrames
import org.phonebridge.pairing.PairingSession
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.Socket
import java.security.KeyStore
import java.security.SecureRandom
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.TrustManagerFactory

@RunWith(AndroidJUnit4::class)
class ServiceTests {
    @Test fun phoneStorageOptionKeepsExistingFolderIndexesAndUsesSharedStorageRoot() {
        assertEquals(listOf("Music", "DCIM", "Pictures", "Download", "Movies", "Documents", "Android/media"), SharedFolders.names)
        assertEquals(SharedFolders.names.size + 1, SharedFolders.count)
        @Suppress("DEPRECATION")
        val root = android.os.Environment.getExternalStorageDirectory().canonicalFile
        assertEquals(root, SharedFolders.selected(SharedFolders.count - 1).canonicalFile)
    }

    @Test fun stoppedServiceLoadsAndRemovesPersistedComputer() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val identity = TlsHelper.openIdentity(context)
        val store = if (identity.created) PairingStore.initializeForNewIdentity(context, identity.fingerprint)
            else PairingStore.openExisting(context, identity.fingerprint)
        assertTrue(store.snapshot().clients.isEmpty())
        val client = "101112131415161718191a1b1c1d1e1f"
        val token = ByteArray(32).also { SecureRandom().nextBytes(it) }
        store.approve(client, "Synthetic PC", token, store.snapshot().revision)
        val connected = CountDownLatch(1)
        var binder: SharingService.LocalBinder? = null
        val connection = object : ServiceConnection {
            override fun onServiceConnected(name: ComponentName, service: IBinder) { binder = service as SharingService.LocalBinder; connected.countDown() }
            override fun onServiceDisconnected(name: ComponentName) { binder = null }
        }
        assertTrue(context.bindService(Intent(context, SharingService::class.java), connection, Context.BIND_AUTO_CREATE))
        try {
            assertTrue(connected.await(10, TimeUnit.SECONDS))
            val loaded = android.os.SystemClock.elapsedRealtime() + 10_000
            while (binder?.service?.storedClients?.singleOrNull()?.clientId != client && android.os.SystemClock.elapsedRealtime() < loaded) Thread.sleep(20)
            assertEquals(client, binder?.service?.storedClients?.single()?.clientId)
            assertFalse(binder?.service?.sharingEnabled ?: true)
            binder?.remove(client)
            val removed = android.os.SystemClock.elapsedRealtime() + 10_000
            while (binder?.service?.storedClients?.isNotEmpty() == true && android.os.SystemClock.elapsedRealtime() < removed) Thread.sleep(20)
            assertTrue(binder?.service?.storedClients?.isEmpty() == true)
            try { store.authenticate(client, token); fail("removed token remained authorized") }
            catch (error: org.phonebridge.credentials.StoreException) { assertEquals(org.phonebridge.credentials.StoreError.UNAUTHORIZED, error.error) }
        } finally {
            token.fill(0)
            context.unbindService(connection)
            context.stopService(Intent(context, SharingService::class.java))
        }
    }

    @Test fun batteryOptimizationRequestTargetsOnlyThisPackage() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val request = BatteryOptimizationPolicy.requestIntent(context.packageName)
        assertEquals(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, request.action)
        assertEquals("package:${context.packageName}", request.data.toString())
        assertNull(request.component)
    }

    @Test fun sharingRuntimeLocksRemainHeldUntilExplicitClose() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        SharingRuntimeLocks(context).use { locks ->
            assertFalse(locks.wakeHeld)
            assertFalse(locks.multicastHeld)
            locks.acquire()
            assertTrue(locks.wakeHeld)
            assertTrue(locks.multicastHeld)
        }
        SharingRuntimeLocks(context).use { locks ->
            locks.acquire()
            locks.close()
            assertFalse(locks.wakeHeld)
            assertFalse(locks.multicastHeld)
        }
    }

    private class Credentials(val client: String, val attempt: String, val grant: ByteArray) : AutoCloseable {
        val token = ByteArray(32).also { SecureRandom().nextBytes(it) }
        val bearer get() = "Bearer ${CredentialEncoding.encode(grant)}"
        val basic get() = "Basic " + android.util.Base64.encodeToString("pbng-$client:${CredentialEncoding.encode(token)}".toByteArray(Charsets.US_ASCII), android.util.Base64.NO_WRAP)
        fun body(name: String = "合成电脑") = JSONObject().put("client_id", client).put("client_name", name).put("credential", CredentialEncoding.encode(token)).toString().toByteArray(Charsets.UTF_8)
        override fun close() { token.fill(0); grant.fill(0) }
    }
    private data class Reply(val code: Int, val body: String)
    private class Fixture(deletionLifetimeMillis: Long = 30_000, val shareReady: AtomicBoolean = AtomicBoolean(true)) : AutoCloseable {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val name = "svc-${UUID.randomUUID()}"
        val root = File(context.filesDir, name).also { check(it.mkdir()) }
        val identity = TlsIdentityStore(File(context.noBackupFilesDir, "$name-tls"), name, File(root, "unused.p12")).open(true)
        var failCommit = false
        val store = PairingStore.testStore(context, identity.fingerprint, name) { stage ->
            if (failCommit && stage == CommitStage.FLUSHED) throw java.io.IOException("synthetic failure")
        }.also { it.initialize() }
        val failed = AtomicBoolean(false)
        private val connections = ClientConnections(store, {}, { failed.set(true) })
        var pairing: PairingController? = null
        val server = AuthorizedServer(0, root, identity.fingerprint, { pairing!! }, connections, { shareReady.get() }, deletionLifetimeMillis)
        val ssl: SSLContext
        init {
            File(root, "origin.txt").writeText("phone-origin", Charsets.UTF_8)
            File(root, "a+b.txt").writeText("literal-plus", Charsets.UTF_8)
            server.makeSecure(TlsHelper.socketFactory(identity), null); server.start(5_000, false)
            resetPairing()
            val trust = KeyStore.getInstance(KeyStore.getDefaultType()).apply { load(null); setCertificateEntry("synthetic-device", identity.authority) }
            val managers = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm()).apply { init(trust) }
            ssl = SSLContext.getInstance("TLS").apply { init(null, managers.trustManagers, null) }
        }
        fun resetPairing() {
            pairing?.close()
            pairing = PairingController(store, identity.authority.encoded, server.listeningPort, { _, _ -> }, {}, { failed.set(true) })
        }
        fun connect(hostname: String = "127.0.0.1"): SSLSocket = (ssl.socketFactory.createSocket(hostname, server.listeningPort) as SSLSocket).apply {
            soTimeout = 10_000
            sslParameters = sslParameters.apply { endpointIdentificationAlgorithm = "HTTPS" }
            startHandshake()
        }
        fun request(method: String, path: String, auth: String?, body: ByteArray = byteArrayOf(), extra: String = "", type: String = "application/json"): Reply {
            val headers = "$method $path HTTP/1.1\r\nHost: 127.0.0.1:${server.listeningPort}\r\n" +
                (auth?.let { "Authorization: $it\r\n" } ?: "") + "Content-Type: $type\r\nContent-Length: ${body.size}\r\n$extra" + "Connection: close\r\n\r\n"
            return raw(headers.toByteArray(Charsets.US_ASCII) + body)
        }
        fun raw(bytes: ByteArray): Reply = connect().use { socket ->
            socket.outputStream.write(bytes); socket.outputStream.flush()
            val result = ByteArrayOutputStream(); val buffer = ByteArray(4096)
            while (true) { val n = socket.inputStream.read(buffer); if (n < 0) break; result.write(buffer, 0, n); check(result.size() < 2_000_000) }
            val text = result.toString("UTF-8"); if (text.isEmpty()) throw java.io.EOFException()
            val line = text.substringBefore("\r\n")
            Reply(line.split(' ')[1].toInt(), text.substringAfter("\r\n\r\n"))
        }
        fun exchange(view: PairingView, code: String = view.code!!, tail: Boolean = false): Credentials {
            val window = view.windowId.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
            Socket("127.0.0.1", view.port).use { socket ->
                socket.soTimeout = 10_000
                PairingSession.createWindows(window, code.toCharArray()).use { session ->
                    for (type in listOf(2, 0x12, 0x22, 0x32)) {
                        socket.outputStream.write(session.createNextFrame()); socket.outputStream.flush()
                        if (type == 0x32) { if (tail) socket.outputStream.write(1); socket.shutdownOutput() }
                        fun exact(count: Int): ByteArray {
                            val data = ByteArray(count); var at = 0
                            while (at < count) { val n = socket.inputStream.read(data, at, count - at); if (n <= 0) throw java.io.EOFException(); at += n }; return data
                        }
                        val header = exact(9); session.acceptFrame(header + exact(PairingFrames.length(header, type)))
                    }
                    check(socket.inputStream.read() == -1)
                    session.takeConfirmation().use { confirmation ->
                        check(confirmation.candidateCaDer.contentEquals(identity.authority.encoded))
                        return Credentials(CredentialEncoding.hex(confirmation.clientId), CredentialEncoding.hex(confirmation.attemptId), confirmation.copyGrant())
                    }
                }
            }
        }
        fun paired(): Credentials {
            val credential = exchange(pairing!!.openWindow())
            assertEquals(202, request("POST", "/phonebridge/v1/pairing/${credential.attempt}", credential.bearer, credential.body()).code)
            pairing!!.decide(credential.attempt, true)
            assertEquals(200, request("GET", "/phonebridge/v1/session", credential.basic).code)
            return credential
        }
        fun authenticate(socket: Socket, credentials: Credentials) { connections.authenticate(socket, credentials.client, credentials.token) }
        fun updateMode(client: String, mode: AccessMode) { connections.updateMode(client, mode) }
        fun remove(client: String) { connections.remove(client) }
        override fun close() { pairing?.close(); connections.close(); server.stop() }
    }
    @Test fun approvalPersistsBeforeReadAndWritesStayUnavailable() {
        Fixture().use { f -> f.exchange(f.pairing!!.openWindow()).use { c ->
            val path = "/phonebridge/v1/pairing/${c.attempt}"
            assertEquals(409, f.request("GET", path, c.bearer).code)
            assertEquals(202, f.request("POST", path, c.bearer, c.body()).code)
            assertTrue(f.store.snapshot().clients.isEmpty())
            assertEquals(401, f.request("GET", "/origin.txt", c.basic).code)
            f.pairing!!.decide(c.attempt, true)
            assertEquals(ClientState.ACTIVE, f.store.snapshot().clients.single().state)
            val session = f.request("GET", "/phonebridge/v1/session", c.basic)
            assertEquals(200, session.code); assertEquals("safe", JSONObject(session.body).getString("mode"))
            assertEquals("phone-origin", f.request("GET", "/origin.txt", c.basic).body)
            assertEquals("literal-plus", f.request("GET", "/a+b.txt", c.basic).body)
            assertEquals("hone", f.request("GET", "/origin.txt", c.basic, extra = "Range: bytes=1-4\r\n").body)
            assertEquals(207, f.request("PROPFIND", "/", c.basic, extra = "Depth: 1\r\n").code)
            assertEquals(200, f.request("POST", path, c.bearer, c.body()).code)
            assertEquals(409, f.request("POST", path, c.bearer, c.body("changed")).code)
            assertEquals(403, f.request("DELETE", "/origin.txt", c.basic).code)
            assertEquals("phone-origin", File(f.root, "origin.txt").readText())
            assertEquals(404, f.request("PROPFIND", "/%70honebridge/unknown", c.basic).code)
            assertEquals(401, f.request("GET", "/origin.txt", c.bearer).code)
        } }
    }
    @Test fun pairingWorksBeforeSharingAndFilesStayClosedUntilSharingStarts() {
        Fixture(shareReady = AtomicBoolean(false)).use { f -> f.exchange(f.pairing!!.openWindow()).use { c ->
            val path = "/phonebridge/v1/pairing/${c.attempt}"
            assertEquals(202, f.request("POST", path, c.bearer, c.body()).code)
            f.pairing!!.decide(c.attempt, true)
            val stopped = f.request("GET", "/phonebridge/v1/session", c.basic)
            assertEquals(200, stopped.code)
            assertFalse(JSONObject(stopped.body).getBoolean("share_ready"))
            assertEquals(409, f.request("GET", "/origin.txt", c.basic).code)
            f.shareReady.set(true)
            assertTrue(JSONObject(f.request("GET", "/phonebridge/v1/session", c.basic).body).getBoolean("share_ready"))
            assertEquals(200, f.request("GET", "/origin.txt", c.basic).code)
        } }
    }
    @Test fun safeWritesCannotOverwriteAndConfirmedDeleteIsSingleUse() {
        Fixture().use { f -> f.paired().use { c ->
            assertEquals(201, f.request("PUT", "/upload.txt", c.basic, "complete".toByteArray(), type = "application/octet-stream").code)
            assertEquals("complete", File(f.root, "upload.txt").readText())
            assertEquals(412, f.request("PUT", "/origin.txt", c.basic, "replacement".toByteArray(), type = "application/octet-stream").code)
            assertEquals("phone-origin", File(f.root, "origin.txt").readText())
            assertEquals(201, f.request("MKCOL", "/new-dir", c.basic).code)
            assertEquals(201, f.request("MOVE", "/upload.txt", c.basic, extra = "Destination: /renamed.txt\r\nOverwrite: T\r\n").code)
            assertFalse(File(f.root, "upload.txt").exists()); assertEquals("complete", File(f.root, "renamed.txt").readText())
            assertEquals(412, f.request("MOVE", "/renamed.txt", c.basic, extra = "Destination: /origin.txt\r\nOverwrite: T\r\n").code)
            assertEquals(201, f.request("COPY", "/origin.txt", c.basic, extra = "Destination: /copy.txt\r\nOverwrite: T\r\n").code)
            assertEquals(412, f.request("COPY", "/origin.txt", c.basic, extra = "Destination: /copy.txt\r\nOverwrite: T\r\n").code)
            assertEquals(400, f.request("POST", "/phonebridge/v1/deletions", c.basic,
                "{\"path\":\"/origin.txt\",\"path\":\"/copy.txt\"}".toByteArray()).code)
            assertEquals(400, f.request("POST", "/phonebridge/v1/deletions", c.basic, byteArrayOf(0xff.toByte())).code)
            val prepared = f.request("POST", "/phonebridge/v1/deletions", c.basic,
                JSONObject().put("path", "/renamed.txt").toString().toByteArray())
            assertEquals(200, prepared.code)
            val id = JSONObject(prepared.body).getString("confirmation_id")
            assertTrue(File(f.root, "renamed.txt").exists())
            assertEquals(204, f.request("DELETE", "/phonebridge/v1/deletions/$id", c.basic).code)
            assertFalse(File(f.root, "renamed.txt").exists())
            assertEquals(404, f.request("DELETE", "/phonebridge/v1/deletions/$id", c.basic).code)
        } }
    }
    @Test fun accessModeChangePersistsAndClosesThePreviousConnection() {
        Fixture().use { f -> f.paired().use { c ->
            Socket().use { previous ->
                f.authenticate(previous, c)
                assertFalse(previous.isClosed)
                f.updateMode(c.client, AccessMode.READ_ONLY)
                assertTrue(previous.isClosed)
            }
            assertEquals("readOnly", JSONObject(f.request("GET", "/phonebridge/v1/session", c.basic).body).getString("mode"))
            assertEquals(403, f.request("PUT", "/origin.txt", c.basic, "blocked".toByteArray(), type = "application/octet-stream").code)
            assertEquals("phone-origin", File(f.root, "origin.txt").readText())

            f.updateMode(c.client, AccessMode.READ_WRITE)
            assertEquals("readWrite", JSONObject(f.request("GET", "/phonebridge/v1/session", c.basic).body).getString("mode"))
            assertEquals(204, f.request("PUT", "/origin.txt", c.basic, "replacement".toByteArray(), type = "application/octet-stream").code)
            assertEquals("replacement", File(f.root, "origin.txt").readText())
        } }
    }
    @Test fun changedAndExpiredDeletionConfirmationsFailClosed() {
        Fixture(50).use { f -> f.paired().use { c ->
            fun prepare(): String {
                val response = f.request("POST", "/phonebridge/v1/deletions", c.basic,
                    JSONObject().put("path", "/origin.txt").toString().toByteArray())
                assertEquals(200, response.code); return JSONObject(response.body).getString("confirmation_id")
            }
            val changed = prepare(); File(f.root, "origin.txt").writeText("changed-by-other-app")
            assertEquals(409, f.request("DELETE", "/phonebridge/v1/deletions/$changed", c.basic).code)
            assertEquals("changed-by-other-app", File(f.root, "origin.txt").readText())
            assertEquals(404, f.request("DELETE", "/phonebridge/v1/deletions/$changed", c.basic).code)
            val expired = prepare(); Thread.sleep(80)
            assertEquals(409, f.request("DELETE", "/phonebridge/v1/deletions/$expired", c.basic).code)
            assertTrue(File(f.root, "origin.txt").exists())
        } }
    }
    @Test fun shortUploadAndRejectedCommitLeaveDestinationIntact() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val root = File(context.filesDir, "storage-${UUID.randomUUID()}").also { check(it.mkdir()) }
        val file = File(root, "target.txt").also { it.writeText("original") }
        SharedStorage(root).use { storage ->
            try { storage.put(SharedPath.parse("/target.txt"), "short".byteInputStream(), 10); fail("short body committed") }
            catch (_: java.io.EOFException) { }
            assertEquals("original", file.readText())
            try {
                storage.put(SharedPath.parse("/target.txt"), "replacement".byteInputStream(), 11, commit = { throw ApiFailure(401, "unauthorized") })
                fail("rejected commit completed")
            } catch (e: ApiFailure) { assertEquals(401, e.status) }
            assertEquals("original", file.readText())
        }
        root.deleteRecursively()
    }
    @Test fun strictJsonAndHeadersRejectAmbiguousRequests() {
        Fixture().use { f -> f.exchange(f.pairing!!.openWindow()).use { c ->
            val path = "/phonebridge/v1/pairing/${c.attempt}"
            val good = c.body().toString(Charsets.UTF_8)
            val bad = listOf("$good{}", good.dropLast(1)+",\"extra\":\"x\"}", good.replace("\"client_id\":", "\"client_id\":\"${c.client}\",\"client_id\":"), "[]", "{\"client_id\":1}")
            for (text in bad) assertEquals(400, f.request("POST", path, c.bearer, text.toByteArray()).code)
            assertEquals(400, f.request("POST", path, c.bearer, byteArrayOf(255.toByte())).code)
            assertEquals(400, f.request("POST", path, c.bearer, c.body(), type = "text/plain").code)
            assertEquals(400, f.request("POST", path, c.bearer, c.body(), extra = "Authorization: ${c.bearer}\r\n").code)
            assertEquals(400, f.request("POST", path, c.bearer, c.body(), extra = "Content-Length: 1\r\n").code)
            assertEquals(401, f.request("POST", path, "Bearer ${CredentialEncoding.encode(ByteArray(32))}", ByteArray(2049)).code)
            assertEquals(400, f.request("POST", path, c.bearer, ByteArray(2049)).code)
            assertEquals(400, f.request("POST", path, c.bearer, c.body(), extra = "Transfer-Encoding: chunked\r\n").code)
            assertEquals(400, f.request("GET", "/%ff", c.bearer).code)
            assertTrue(f.store.snapshot().clients.isEmpty()); assertNull(f.pairing!!.view()!!.pendingName)
        } }
    }
    @Test fun wrongCodeAndTrailingFrameCannotObtainGrant() {
        Fixture().use { f ->
            val view = f.pairing!!.openWindow(); val wrong = view.code!!.dropLast(1) + if (view.code.last() == '0') '1' else '0'
            try { f.exchange(view, wrong).close(); fail("wrong code accepted") } catch (_: java.io.IOException) { } catch (_: org.phonebridge.pairing.PairingProtocolException) { }
            assertNull(f.pairing!!.view()!!.attemptId)
            try { f.exchange(view, tail = true).close(); fail("trailing frame accepted") } catch (_: java.io.IOException) { } catch (_: org.phonebridge.pairing.PairingProtocolException) { }
            assertNull(f.pairing!!.view()!!.attemptId)
            f.exchange(view).use { assertEquals(3, f.pairing!!.view()!!.attemptsUsed) }
        }
    }
    @Test fun fifthAcceptedFailureClosesWindowAndDoesNotReopen() {
        Fixture().use { f ->
            val view = f.pairing!!.openWindow()
            for (attempt in 1..5) {
                Socket("127.0.0.1", view.port).use { it.shutdownOutput() }
                val deadline = android.os.SystemClock.elapsedRealtime() + 5_000
                while (f.pairing!!.view()?.attemptsUsed?.let { it < attempt } == true && android.os.SystemClock.elapsedRealtime() < deadline) Thread.sleep(10)
            }
            val deadline = android.os.SystemClock.elapsedRealtime() + 5_000
            while (f.pairing!!.view() != null && android.os.SystemClock.elapsedRealtime() < deadline) Thread.sleep(10)
            assertNull(f.pairing!!.view()); assertTrue(f.store.snapshot().clients.isEmpty())
            try { f.pairing!!.openWindow(); fail("cooldown bypass") } catch (e: ApiFailure) { assertEquals(409, e.status) }
        }
    }
    @Test fun rejectCancelAndClosedGrantCannotActivate() {
        for (decision in listOf("reject", "cancel", "close")) Fixture().use { f -> f.exchange(f.pairing!!.openWindow()).use { c ->
            val path = "/phonebridge/v1/pairing/${c.attempt}"
            assertEquals(202, f.request("POST", path, c.bearer, c.body()).code)
            val expected = when (decision) {
                "reject" -> { f.pairing!!.decide(c.attempt, false); 403 }
                "cancel" -> { assertEquals(410, f.request("DELETE", path, c.bearer).code); 410 }
                else -> { f.pairing!!.cancelWindow(); 401 }
            }
            assertEquals(expected, f.request("POST", path, c.bearer, c.body()).code)
            assertTrue(f.store.snapshot().clients.isEmpty()); assertEquals(401, f.request("GET", "/phonebridge/v1/session", c.basic).code)
        } }
    }
    @Test fun selfRevocationPersistsAndInvalidatesOwnToken() {
        Fixture().use { f -> f.paired().use { c ->
            assertEquals(204, f.request("DELETE", "/phonebridge/v1/pairings/self", c.basic).code)
            assertEquals(ClientState.REVOKED, f.store.snapshot().clients.single().state)
            assertEquals(401, f.request("GET", "/origin.txt", c.basic).code)
            assertEquals(401, f.request("GET", "/phonebridge/v1/session", c.basic).code)
            assertEquals(401, f.request("DELETE", "/phonebridge/v1/pairings/self", c.basic).code)
        } }
    }
    @Test fun localRemovalDeletesPairingAndRejectsOldToken() {
        Fixture().use { f -> f.paired().use { c ->
            f.remove(c.client)
            assertTrue(f.store.snapshot().clients.isEmpty())
            assertEquals(401, f.request("GET", "/phonebridge/v1/session", c.basic).code)
        } }
    }
    @Test fun revokeCancelsAnAlreadyStreamingDownload() {
        Fixture().use { f -> f.paired().use { c ->
            val size = 64 * 1024 * 1024
            File(f.root, "large.bin").outputStream().use { output -> val chunk = ByteArray(1024 * 1024) { (it % 251).toByte() }; repeat(64) { output.write(chunk) } }
            f.connect().use { active ->
                active.outputStream.write("GET /large.bin HTTP/1.1\r\nHost: 127.0.0.1\r\nAuthorization: ${c.basic}\r\nConnection: close\r\n\r\n".toByteArray())
                val header = ByteArrayOutputStream()
                while (!header.toString("US-ASCII").endsWith("\r\n\r\n")) { val b = active.inputStream.read(); check(b >= 0); header.write(b); check(header.size() <= 8192) }
                assertTrue(header.toString("US-ASCII").startsWith("HTTP/1.1 200"))
                val revokeStarted = android.os.SystemClock.elapsedRealtime()
                assertEquals(204, f.request("DELETE", "/phonebridge/v1/pairings/self", c.basic).code)
                assertTrue("revocation exceeded request deadline", android.os.SystemClock.elapsedRealtime() - revokeStarted < 5_000)
                var read = 0
                try { val chunk = ByteArray(16384); while (true) { val n = active.inputStream.read(chunk); if (n < 0) break; read += n } } catch (_: java.io.IOException) { }
                assertTrue("revoked stream reached full content", read < size)
            }
        } }
    }
    @Test fun storedCorruptionFailsClosedForAllRequests() {
        Fixture().use { f -> f.paired().use { c ->
            val record = File(f.context.noBackupFilesDir, "${f.name}/records.bin")
            val bytes = record.readBytes(); bytes[bytes.lastIndex] = (bytes.last().toInt() xor 1).toByte(); record.writeBytes(bytes)
            assertEquals(503, f.request("GET", "/phonebridge/v1/session", c.basic).code)
            assertTrue(f.failed.get()); assertEquals(503, f.request("GET", "/origin.txt", c.basic).code)
        } }
    }
    @Test fun temporaryGrantDiesWhenControllerRestartsButTokenRemains() {
        Fixture().use { f -> f.paired().use { c ->
            f.resetPairing()
            assertEquals(401, f.request("GET", "/phonebridge/v1/pairing/${c.attempt}", c.bearer).code)
            assertEquals(200, f.request("GET", "/phonebridge/v1/session", c.basic).code)
        } }
    }
    @Test fun cancellationAndApprovalHaveOneWinningState() {
        Fixture().use { f -> f.exchange(f.pairing!!.openWindow()).use { c ->
            val path = "/phonebridge/v1/pairing/${c.attempt}"
            assertEquals(202, f.request("POST", path, c.bearer, c.body()).code)
            val start = CountDownLatch(1); var decision: Int? = null
            val approving = Thread { start.await(); decision = try { f.pairing!!.decide(c.attempt, true); 200 } catch (e: ApiFailure) { e.status } }
            approving.start(); start.countDown(); val cancelled = f.request("DELETE", path, c.bearer).code; approving.join(10_000)
            assertFalse(approving.isAlive)
            if (decision == 200) { assertEquals(409, cancelled); assertEquals(200, f.request("GET", "/phonebridge/v1/session", c.basic).code) }
            else { assertEquals(409, decision ?: 0); assertEquals(410, cancelled); assertTrue(f.store.snapshot().clients.isEmpty()) }
        } }
    }
    @Test fun realWindowExpiresAndCannotBeExtendedByPolling() {
        Fixture().use { f -> f.exchange(f.pairing!!.openWindow()).use { c ->
            val path = "/phonebridge/v1/pairing/${c.attempt}"
            assertEquals(202, f.request("POST", path, c.bearer, c.body()).code)
            val deadline = android.os.SystemClock.elapsedRealtime() + 125_000
            while (f.pairing!!.view() != null && android.os.SystemClock.elapsedRealtime() < deadline) {
                val status = f.request("GET", path, c.bearer).code
                assertTrue(status == 202 || status == 401)
                Thread.sleep(1_010)
            }
            assertNull(f.pairing!!.view()); assertEquals(401, f.request("GET", path, c.bearer).code)
            assertTrue(f.store.snapshot().clients.isEmpty())
        } }
    }
    @Test fun strictTlsRejectsWrongIdentityAndHostname() {
        Fixture().use { f -> Fixture().use { other ->
            for ((factory, name) in listOf(other.ssl.socketFactory to "127.0.0.1", f.ssl.socketFactory to "wrong.invalid")) {
                Socket("127.0.0.1", f.server.listeningPort).use { raw ->
                    (factory.createSocket(raw, name, f.server.listeningPort, true) as SSLSocket).use { socket ->
                        socket.soTimeout = 5_000; socket.sslParameters = socket.sslParameters.apply { endpointIdentificationAlgorithm = "HTTPS" }
                        try { socket.startHandshake(); fail("TLS accepted wrong identity or endpoint") } catch (_: javax.net.ssl.SSLException) { }
                    }
                }
            }
        } }
    }
    @Test fun revokingOneComputerKeepsTheOtherAuthorized() {
        Fixture().use { f -> f.paired().use { first ->
            f.pairing!!.cancelWindow(); Thread.sleep(10_010)
            f.paired().use { second ->
                assertEquals(2, f.store.snapshot().clients.count { it.state == ClientState.ACTIVE })
                assertEquals(204, f.request("DELETE", "/phonebridge/v1/pairings/self", first.basic).code)
                assertEquals(401, f.request("GET", "/origin.txt", first.basic).code)
                assertEquals(200, f.request("GET", "/origin.txt", second.basic).code)
            }
        } }
    }
    @Test fun approvalWriteFailureCannotAcknowledgeActivation() {
        Fixture().use { f -> f.exchange(f.pairing!!.openWindow()).use { c ->
            assertEquals(202, f.request("POST", "/phonebridge/v1/pairing/${c.attempt}", c.bearer, c.body()).code)
            f.failCommit = true
            try { f.pairing!!.decide(c.attempt, true); fail("failed write acknowledged") }
            catch (e: ApiFailure) { assertEquals(503, e.status) }
            assertTrue(f.failed.get()); assertNull(f.pairing!!.view())
            assertEquals(503, f.request("GET", "/phonebridge/v1/session", c.basic).code)
        } }
    }
    @Test fun revokeWriteFailureClosesConnectionWithoutSuccessAcknowledgment() {
        Fixture().use { f -> f.paired().use { c ->
            f.failCommit = true
            // On storage failure the already authenticated socket is terminated, never acknowledged as revoked.
            try { val reply = f.request("DELETE", "/phonebridge/v1/pairings/self", c.basic); assertEquals(503, reply.code) }
            catch (_: java.io.IOException) { }
            // Transport abort is intentionally visible before the following fatal callback is scheduled.
            val deadline = android.os.SystemClock.elapsedRealtime() + 1_000
            while (!f.failed.get() && android.os.SystemClock.elapsedRealtime() < deadline) Thread.sleep(5)
            assertTrue(f.failed.get())
            assertEquals(503, f.request("GET", "/phonebridge/v1/session", c.basic).code)
        } }
    }
    @Test fun slowHeadersAndIncompletePairingHaveAbsoluteDeadlines() {
        Fixture().use { f ->
            val view = f.pairing!!.openWindow()
            Socket("127.0.0.1", view.port).use { socket ->
                socket.soTimeout = 8_000
                val start = android.os.SystemClock.elapsedRealtime()
                socket.outputStream.write(byteArrayOf(80))
                assertEquals(-1, socket.inputStream.read())
                assertTrue(android.os.SystemClock.elapsedRealtime() - start < 7_500)
            }
            f.connect().use { socket ->
                val start = android.os.SystemClock.elapsedRealtime()
                socket.outputStream.write("GET / HTTP/1.1\r\nHost: ".toByteArray())
                try { assertEquals(-1, socket.inputStream.read()) } catch (_: javax.net.ssl.SSLException) { }
                assertTrue(android.os.SystemClock.elapsedRealtime() - start < 7_500)
            }
        }
    }
}
