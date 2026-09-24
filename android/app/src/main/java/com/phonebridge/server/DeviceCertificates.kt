package com.phonebridge.server

// PhoneBridge NG, 2026-09-19. SPDX-License-Identifier: GPL-3.0-or-later
import org.bouncycastle.asn1.DEROctetString
import org.bouncycastle.asn1.x500.X500Name
import org.bouncycastle.asn1.x509.*
import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder
import java.math.BigInteger
import java.net.InetAddress
import java.security.PrivateKey
import java.security.PublicKey
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.Date

internal object DeviceCertificates {
    private const val DAY = 86_400_000L
    private fun serial() = BigInteger(159, SecureRandom()).add(BigInteger.ONE)

    fun authority(publicKey: PublicKey, key: PrivateKey, now: Long = System.currentTimeMillis()): X509Certificate {
        val name = X500Name("CN=PhoneBridge NG Device Identity")
        val builder = JcaX509v3CertificateBuilder(name, serial(), Date(now - 300_000), Date(now + 3650 * DAY), name, publicKey)
        builder.addExtension(Extension.basicConstraints, true, BasicConstraints(0))
        builder.addExtension(Extension.keyUsage, true, KeyUsage(KeyUsage.keyCertSign or KeyUsage.cRLSign))
        return JcaX509CertificateConverter().getCertificate(builder.build(JcaContentSignerBuilder("SHA256withRSA").build(key)))
    }

    fun server(authority: X509Certificate, signer: PrivateKey, publicKey: PublicKey,
               addresses: Set<InetAddress>, now: Long = System.currentTimeMillis()): X509Certificate {
        require(addresses.isNotEmpty() && addresses.size <= 128)
        require(addresses.none { it.isAnyLocalAddress || it.isMulticastAddress })
        authority.checkValidity(Date(now))
        val builder = JcaX509v3CertificateBuilder(authority, serial(), Date(now - 300_000),
            Date(minOf(now + DAY, authority.notAfter.time)), X500Name("CN=PhoneBridge NG File Server"), publicKey)
        builder.addExtension(Extension.basicConstraints, true, BasicConstraints(false))
        builder.addExtension(Extension.keyUsage, true, KeyUsage(KeyUsage.digitalSignature or KeyUsage.keyEncipherment))
        builder.addExtension(Extension.extendedKeyUsage, false, ExtendedKeyUsage(KeyPurposeId.id_kp_serverAuth))
        val names = addresses.map { it.address }.distinctBy { it.toList() }
            .map { GeneralName(GeneralName.iPAddress, DEROctetString(it)) }.toTypedArray()
        builder.addExtension(Extension.subjectAlternativeName, false, GeneralNames(names))
        return JcaX509CertificateConverter().getCertificate(builder.build(JcaContentSignerBuilder("SHA256withRSA").build(signer)))
    }
}
