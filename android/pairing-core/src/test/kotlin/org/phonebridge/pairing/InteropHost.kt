package org.phonebridge.pairing

import java.io.FileInputStream
import java.security.MessageDigest
import java.security.cert.CertificateFactory
import java.util.Base64
import kotlin.system.exitProcess

/** Synthetic stdin/stdout harness only. No socket, Android service or persistent credential. */
object InteropHost {
    @JvmStatic fun main(args: Array<String>) {
        if(args.size!=2 || args[0] !in listOf("00123456","00123457","00000000")) exitProcess(3)
        try {
            val ca=FileInputStream(args[1]).use { CertificateFactory.getInstance("X.509").generateCertificate(it).encoded }
            PairingSession.createAndroid(ByteArray(16) { it.toByte() },args[0].toCharArray(),8273,ca).use { session ->
                for (type in intArrayOf(1,0x11,0x21,0x31)) {
                    val line=StringBuilder()
                    while(true) {
                        val c=System.`in`.read()
                        if(c<0 || line.length>=12000) throw PairingProtocolException()
                        if(c==10) break
                        if(c!=13) line.append(c.toChar())
                    }
                    val reader=FrameAccumulator(type)
                    Base64.getDecoder().decode(line.toString()).forEach { reader.feed(byteArrayOf(it)) }
                    reader.endOfInput();session.acceptFrame(reader.getFrame())
                    println(Base64.getEncoder().encodeToString(session.createNextFrame()))
                }
                session.takeConfirmation().use { result ->
                    val grant=result.copyGrant()
                    println("OK:"+hex(MessageDigest.getInstance("SHA-256").digest(grant))+":"+
                        hex(MessageDigest.getInstance("SHA-256").digest(result.candidateCaDer))+":"+hex(result.clientId)+":"+
                        hex(result.attemptId)+":"+result.httpsPort)
                    grant.fill(0)
                }
            }
        } catch (_: Exception) { println("REJECT");exitProcess(2) }
    }
    private fun hex(bytes: ByteArray)=bytes.joinToString("") { "%02x".format(it.toInt() and 255) }
}
