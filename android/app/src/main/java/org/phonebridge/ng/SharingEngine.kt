package org.phonebridge.ng

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import com.phonebridge.server.TlsHelper
import org.phonebridge.credentials.AccessMode
import org.phonebridge.credentials.PairingStore
import org.phonebridge.credentials.StoreSnapshot
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean

internal class SharingEngine(context: Context, root: File, port: Int, private val failed: () -> Unit) : AutoCloseable {
    private val identity = TlsHelper.openIdentity(context)
    val fingerprint = identity.fingerprint
    private val store = if (identity.created) PairingStore.initializeForNewIdentity(context, fingerprint) else PairingStore.openExisting(context, fingerprint)
    @Volatile var clients: StoreSnapshot = store.snapshot()
        private set
    private val fatal = AtomicBoolean(false)
    private val viewGate = Any()
    private val publisher = DiscoveryPublisher(context, fingerprint)
    private val connections = ClientConnections(store, ::publishClients, ::storageFailed)
    private var pairingInstance: PairingController? = null
    private val server = AuthorizedServer(port, root, fingerprint, { pairingInstance ?: throw ApiFailure(503, "storage_failure") }, connections)
    val pairing: PairingController get() = pairingInstance ?: throw ApiFailure(503, "storage_failure")
    val httpsPort: Int get() = server.listeningPort
    init {
        try {
            server.makeSecure(TlsHelper.socketFactory(identity), null)
            server.start(5_000, false)
            pairingInstance = PairingController(store, identity.authority.encoded, httpsPort,
                { id, pairPort -> publisher.publish(httpsPort, id, pairPort) }, ::publishClients, ::storageFailed)
            publisher.publish(httpsPort, null, 0)
        } catch (e: Exception) { close(); throw e }
    }
    private fun storageFailed() { if (fatal.compareAndSet(false, true)) failed() }
    private fun publishClients(snapshot: StoreSnapshot) = synchronized(viewGate) { if (snapshot.revision > clients.revision) clients = snapshot }
    fun revoke(client: String) { connections.revoke(client) }
    fun updateMode(client: String, mode: AccessMode) { connections.updateMode(client, mode) }
    override fun close() {
        pairingInstance?.close(); pairingInstance = null
        publisher.close(); connections.close(); server.stop()
    }
}

/** Updates are serialized; every callback belongs to its exact registration listener. */
internal class DiscoveryPublisher(context: Context, private val fingerprint: String) : AutoCloseable {
    private val manager = context.getSystemService(NsdManager::class.java)
    private val gate = Any()
    private var registration: NsdManager.RegistrationListener? = null
    private var closed = false
    @Volatile var available = false
        private set
    fun publish(port: Int, window: String?, pairingPort: Int) = synchronized(gate) {
        if (closed) return@synchronized
        registration?.let { try { manager.unregisterService(it) } catch (_: Exception) { } }
        available = false
        val listener = object : NsdManager.RegistrationListener {
            override fun onServiceRegistered(info: NsdServiceInfo) = synchronized(gate) {
                if (!closed && registration === this) available = true
                else try { manager.unregisterService(this) } catch (_: Exception) { }
            }
            override fun onRegistrationFailed(info: NsdServiceInfo, errorCode: Int) = synchronized(gate) { if (registration === this) available = false }
            override fun onServiceUnregistered(info: NsdServiceInfo) = synchronized(gate) { if (registration === this) available = false }
            override fun onUnregistrationFailed(info: NsdServiceInfo, errorCode: Int) = synchronized(gate) { if (registration === this) available = false }
        }
        registration = listener
        val info = NsdServiceInfo().apply {
            serviceName = "PhoneBridge NG ${fingerprint.take(8)}"; serviceType = "_phonebridge._tcp."; setPort(port)
            setAttribute("version", "3"); setAttribute("protocol", "https"); setAttribute("auth", "paired-v1")
            setAttribute("deviceName", android.os.Build.MODEL.take(64)); setAttribute("device_id", "pbng-$fingerprint")
            if (window != null) { setAttribute("pairing", "jpake1"); setAttribute("pair_port", pairingPort.toString()); setAttribute("pair_window", window) }
        }
        try { manager.registerService(info, NsdManager.PROTOCOL_DNS_SD, listener) } catch (_: Exception) { registration = null }
    }
    override fun close() = synchronized(gate) {
        closed = true; available = false
        registration?.let { try { manager.unregisterService(it) } catch (_: Exception) { } }; registration = null
    }
}
