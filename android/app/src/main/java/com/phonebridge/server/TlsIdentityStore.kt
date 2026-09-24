package com.phonebridge.server

// PhoneBridge NG, 2026-09-19. SPDX-License-Identifier: GPL-3.0-or-later
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.AtomicFile
import java.io.File
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.PrivateKey
import java.security.PublicKey
import java.security.SecureRandom
import java.security.Signature
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate

internal data class TlsIdentity(val authority: X509Certificate, val authorityKey: PrivateKey,
                              val serverPublicKey: PublicKey, val serverKey: PrivateKey, val created: Boolean) {
    val fingerprint: String get() = MessageDigest.getInstance("SHA-256").digest(authority.encoded)
        .joinToString("") { "%02x".format(it) }
}

internal class TlsIdentityStore(private val directory: File, private val prefix: String, private val legacy: File) {
    private val authorityAlias = "$prefix.authority"
    private val serverAlias = "$prefix.server"
    private val certificateFile = AtomicFile(File(directory, "identity.der"))

    fun open(allowCreate: Boolean): TlsIdentity = synchronized(lock) {
        check(!legacy.exists()) { "Legacy TLS identity requires explicit migration and new pairing" }
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        val authorityExists = store.containsAlias(authorityAlias)
        val serverExists = store.containsAlias(serverAlias)
        val certificateExists = certificateFile.baseFile.exists() || File(directory, "identity.der.bak").exists()
        val created = !authorityExists && !serverExists && !certificateExists
        if (created) {
            check(allowCreate) { "Identity initialization is not authorized" }
            check(directory.mkdirs() || directory.isDirectory)
            // Partial initialization stays incomplete; never replace an identity.
            generateKey(authorityAlias, false)
            generateKey(serverAlias, true)
            val key = store.getKey(authorityAlias, null) as PrivateKey
            val cert = DeviceCertificates.authority(store.getCertificate(authorityAlias).publicKey, key)
            val stream = certificateFile.startWrite()
            try {
                stream.write(cert.encoded)
                certificateFile.finishWrite(stream)
            } catch (e: Exception) {
                certificateFile.failWrite(stream)
                throw e
            }
        } else {
            check(authorityExists && serverExists && certificateExists) { "TLS identity is incomplete; pairing reset required" }
        }
        val bytes = certificateFile.openRead().use { input ->
            // Bound the public metadata before allocating, including corrupted files.
            check(input.channel.size() in 1..65536) { "Invalid identity certificate size" }
            input.readBytes()
        }
        val cert = CertificateFactory.getInstance("X.509").generateCertificate(bytes.inputStream()) as X509Certificate
        cert.checkValidity()
        check(cert.basicConstraints == 0 && cert.keyUsage?.get(5) == true && cert.issuerX500Principal == cert.subjectX500Principal)
        check(cert.publicKey.encoded.contentEquals(store.getCertificate(authorityAlias).publicKey.encoded)) { "Identity key mismatch" }
        cert.verify(cert.publicKey)
        val authorityKey = store.getKey(authorityAlias, null) as PrivateKey
        val serverKey = store.getKey(serverAlias, null) as PrivateKey
        check(authorityKey.encoded == null && serverKey.encoded == null) { "Non-exportable keys required" }
        verifyKey(authorityKey, cert.publicKey)
        val serverPublicKey = store.getCertificate(serverAlias).publicKey
        verifyKey(serverKey, serverPublicKey)
        TlsIdentity(cert, authorityKey, serverPublicKey, serverKey, created)
    }

    private fun generateKey(alias: String, server: Boolean) {
        val spec = KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_SIGN or
            (if (server) KeyProperties.PURPOSE_DECRYPT else 0))
            .setKeySize(2048)
            // TLS computes the handshake digest itself; Keystore must permit raw signing.
            // The identity CA does not need that broader authorization.
            .setDigests(*(if (server) arrayOf(KeyProperties.DIGEST_NONE, KeyProperties.DIGEST_SHA256,
                KeyProperties.DIGEST_SHA384, KeyProperties.DIGEST_SHA512) else arrayOf(KeyProperties.DIGEST_SHA256)))
            .setSignaturePaddings(KeyProperties.SIGNATURE_PADDING_RSA_PKCS1, KeyProperties.SIGNATURE_PADDING_RSA_PSS)
            .setUserAuthenticationRequired(false)
        // Conscrypt's TLS 1.3 RSA-PSS path supplies its own padding through the
        // private-key RSA operation. This authorization stays on the TLS key only.
        if (server) spec.setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE,
            KeyProperties.ENCRYPTION_PADDING_RSA_PKCS1)
        KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_RSA, "AndroidKeyStore").apply {
            initialize(spec.build())
        }.generateKeyPair()
    }

    private fun verifyKey(key: PrivateKey, publicKey: PublicKey) {
        val challenge = ByteArray(32).also { SecureRandom().nextBytes(it) }
        val signer = Signature.getInstance("SHA256withRSA").apply { initSign(key); update(challenge) }
        val signature = signer.sign()
        val verifier = Signature.getInstance("SHA256withRSA").apply { initVerify(publicKey); update(challenge) }
        check(verifier.verify(signature)) { "Identity key validation failed" }
    }

    companion object { private val lock = Any() }
}
