package com.phonebridge.server

// Modified by PhoneBridge NG, 2026-09-19. SPDX-License-Identifier: GPL-3.0-or-later
import android.content.Context
import java.io.File
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.Socket
import java.security.PrivateKey
import java.security.SecureRandom
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLEngine
import javax.net.ssl.SSLServerSocket
import javax.net.ssl.SSLServerSocketFactory
import javax.net.ssl.X509ExtendedKeyManager

object TlsHelper {
    internal fun openIdentity(context: Context): TlsIdentity {
        val root = context.noBackupFilesDir
        return TlsIdentityStore(File(root, "tls-identity-v1"), "org.phonebridge.ng.identity.v1",
            File(context.filesDir, "phonebridge_keystore.p12")).open(!File(root, "pairings-v1").exists())
    }
    internal fun socketFactory(identity: TlsIdentity): SSLServerSocketFactory {
        val manager = AddressKeyManager(identity, ::currentAddresses)
        manager.getCertificateChain("server")
        return SSLContext.getInstance("TLS").apply { init(arrayOf(manager), null, SecureRandom()) }.serverSocketFactory.let { ModernServerSocketFactory(it) }
    }

    internal fun currentAddresses(): Set<InetAddress> {
        val addresses = mutableSetOf(InetAddress.getByName("127.0.0.1"), InetAddress.getByName("::1"))
        val interfaces = NetworkInterface.getNetworkInterfaces()
        while (interfaces.hasMoreElements()) {
            val network = interfaces.nextElement()
            if (!network.isUp) continue
            val entries = network.inetAddresses
            while (entries.hasMoreElements()) {
                val address = entries.nextElement()
                if (!address.isAnyLocalAddress && !address.isMulticastAddress) addresses.add(address)
            }
        }
        return addresses
    }
}

internal class AddressKeyManager(
    private val identity: TlsIdentity,
    private val addresses: () -> Set<InetAddress>
) : X509ExtendedKeyManager() {
    private var currentAddresses = emptySet<String>()
    private var currentCertificate: X509Certificate? = null

    @Synchronized override fun getCertificateChain(alias: String?): Array<X509Certificate>? {
        if (alias != "server") return null
        identity.authority.checkValidity()
        val ips = addresses()
        require(ips.isNotEmpty() && ips.size <= 128) { "Invalid local address set" }
        val keys = ips.map { it.address.joinToString("") { byte -> "%02x".format(byte) } }.toSet()
        val certificate = currentCertificate
        if (certificate == null || currentAddresses != keys || certificate.notAfter.time - System.currentTimeMillis() < 3_600_000L) {
            currentCertificate = DeviceCertificates.server(identity.authority, identity.authorityKey, identity.serverPublicKey, ips)
            currentAddresses = keys
        }
        return arrayOf(currentCertificate!!, identity.authority)
    }

    override fun getPrivateKey(alias: String?): PrivateKey? = if (alias == "server") identity.serverKey else null
    override fun getServerAliases(keyType: String?, issuers: Array<java.security.Principal>?): Array<String>? =
        if (keyType == "RSA") arrayOf("server") else null
    override fun chooseServerAlias(keyType: String?, issuers: Array<java.security.Principal>?, socket: Socket?): String? =
        getServerAliases(keyType, issuers)?.first()
    override fun chooseEngineServerAlias(keyType: String?, issuers: Array<java.security.Principal>?, engine: SSLEngine?): String? =
        getServerAliases(keyType, issuers)?.first()
    override fun getClientAliases(keyType: String?, issuers: Array<java.security.Principal>?): Array<String>? = null
    override fun chooseClientAlias(keyTypes: Array<String>?, issuers: Array<java.security.Principal>?, socket: Socket?): String? = null
}

internal class ModernServerSocketFactory(private val delegate: SSLServerSocketFactory) : SSLServerSocketFactory() {
    private fun configure(socket: ServerSocket): ServerSocket = (socket as SSLServerSocket).apply {
        enabledProtocols = supportedProtocols.filter { it == "TLSv1.2" || it == "TLSv1.3" }.toTypedArray()
        check(enabledProtocols.isNotEmpty()) { "TLS 1.2 or newer is required" }
    }
    override fun getDefaultCipherSuites(): Array<String> = delegate.defaultCipherSuites
    override fun getSupportedCipherSuites(): Array<String> = delegate.supportedCipherSuites
    override fun createServerSocket(): ServerSocket = configure(delegate.createServerSocket())
    override fun createServerSocket(port: Int): ServerSocket = configure(delegate.createServerSocket(port))
    override fun createServerSocket(port: Int, backlog: Int): ServerSocket = configure(delegate.createServerSocket(port, backlog))
    override fun createServerSocket(port: Int, backlog: Int, address: InetAddress): ServerSocket =
        configure(delegate.createServerSocket(port, backlog, address))
}
