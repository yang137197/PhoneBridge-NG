package org.phonebridge.pairing

import java.io.ByteArrayOutputStream
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.concurrent.atomic.AtomicBoolean
import org.bouncycastle.crypto.digests.SHA256Digest
import org.bouncycastle.crypto.generators.HKDFBytesGenerator
import org.bouncycastle.crypto.params.HKDFParameters

enum class PairingState { ACTIVE, CONFIRMED, CONSUMED, FAILED, CLOSED }

/** In-memory protocol only. The host owns deadlines, window budgets and user authorization. */
class PairingSession private constructor(
    private val windows: Boolean, windowInput: ByteArray, codeInput: CharArray, portInput: Int, caInput: ByteArray
) : AutoCloseable {
    private val window: ByteArray
    private val code: CharArray
    private var client: ByteArray
    private var attempt: ByteArray
    private var ca: ByteArray
    private var port: Int
    private var step = 0
    private val transcript = ByteArrayOutputStream()
    private var exchange: PakeExchange? = null
    private val cancelled = AtomicBoolean(false)
    @Volatile var state = PairingState.ACTIVE
        private set
    init {
        if (windowInput.size != 16 || codeInput.size != 8 || codeInput.any { it !in '0'..'9' } ||
            (!windows && (portInput !in 1..65535 || caInput.size !in 1..4096))) throw PairingProtocolException()
        window=windowInput.copyOf(); code=codeInput.copyOf(); ca=caInput.copyOf(); port=portInput
        client=if (windows) randomId() else byteArrayOf(); attempt=if (windows) byteArrayOf() else randomId()
    }
    companion object {
        fun createWindows(window: ByteArray, code: CharArray) = PairingSession(true,window,code,0,byteArrayOf())
        fun createAndroid(window: ByteArray, code: CharArray, httpsPort: Int, caDer: ByteArray) = PairingSession(false,window,code,httpsPort,caDer)
        private fun randomId() = ByteArray(16).also { SecureRandom().nextBytes(it) }
        internal fun hkdf(key: ByteArray, salt: ByteArray, info: ByteArray, length: Int): ByteArray {
            val generator=HKDFBytesGenerator(SHA256Digest())
            generator.init(HKDFParameters(key,salt,info))
            return ByteArray(length).also { generator.generateBytes(it,0,it.size) }
        }
    }

    @Synchronized fun createNextFrame(): ByteArray {
        try {
            guard()
            if ((step % 2 == 0) != windows) throw PairingProtocolException()
            val body = when (step) {
                0 -> byteArrayOf(1)+window+client
                1 -> byteArrayOf(1)+window+client+attempt+byteArrayOf((port ushr 8).toByte(),port.toByte(),(ca.size ushr 8).toByte(),ca.size.toByte())+ca
                2,3 -> exchange!!.round1()
                4,5 -> exchange!!.round2()
                6,7 -> exchange!!.round3()
                else -> throw PairingProtocolException()
            }
            checkCancellation()
            val frame=PairingFrames.encode(PairingFrames.order[step],body); advance(frame); return frame
        } catch (_: Exception) { fail(); throw PairingProtocolException() }
    }

    @Synchronized fun acceptFrame(frame: ByteArray) {
        try {
            guard()
            if ((step % 2 == 0) == windows) throw PairingProtocolException()
            if (frame.size > PairingFrames.HEADER_SIZE+PairingFrames.MAX_PAYLOAD) throw PairingProtocolException()
            val snapshot=frame.copyOf()
            PairingFrames.validate(snapshot,PairingFrames.order[step])
            val body=snapshot.copyOfRange(PairingFrames.HEADER_SIZE,snapshot.size)
            when (step) {
                0 -> {
                    if (body[0] != 1.toByte() || !body.copyOfRange(1,17).contentEquals(window)) throw PairingProtocolException()
                    client=body.copyOfRange(17,33)
                }
                1 -> {
                    if (body[0] != 1.toByte() || !body.copyOfRange(1,17).contentEquals(window) || !body.copyOfRange(17,33).contentEquals(client))
                        throw PairingProtocolException()
                    port=u16(body,49); val size=u16(body,51)
                    if (port == 0 || size !in 1..4096 || body.size != 53+size) throw PairingProtocolException()
                    attempt=body.copyOfRange(33,49); ca=body.copyOfRange(53,body.size)
                }
                2,3 -> exchange!!.acceptRound1(body)
                4,5 -> exchange!!.acceptRound2(body)
                6,7 -> exchange!!.acceptRound3(body)
                else -> throw PairingProtocolException()
            }
            checkCancellation(); advance(snapshot)
        } catch (_: Exception) { fail(); throw PairingProtocolException() }
    }

    private fun advance(frame: ByteArray) {
        if (transcript.size()+frame.size > PairingFrames.MAX_SESSION_BYTES) throw PairingProtocolException()
        transcript.write(frame); step++
        if (step == 2) {
            val context=sha(transcript.toByteArray()).joinToString("") { "%02x".format(it.toInt() and 255) }
            val own="pbng-pair-v1:"+(if (windows) "W:" else "A:")+context
            val peer="pbng-pair-v1:"+(if (windows) "A:" else "W:")+context
            exchange=PakeExchange(own,peer,code); code.fill('\u0000')
        }
        if (step == 8) state=PairingState.CONFIRMED
    }

    /** Only after the final transport send/receive and completion checks. CA validation still required. */
    @Synchronized fun takeConfirmation(): PakeConfirmation {
        var key: ByteArray? = null
        try {
            checkCancellation()
            if (state != PairingState.CONFIRMED) throw PairingProtocolException()
            key=exchange!!.exportKey()
            val grant=hkdf(key,sha(transcript.toByteArray()),"PhoneBridge NG|pairing-grant|v1".toByteArray(Charsets.US_ASCII),32)
            try {
                checkCancellation()
                val result=PakeConfirmation(ca,client,attempt,port,grant)
                state=PairingState.CONSUMED; release(); return result
            } finally { grant.fill(0) }
        } catch (_: Exception) { fail(); throw PairingProtocolException() }
        finally { key?.fill(0) }
    }
    /** Signals cancellation at a protocol boundary, not interruption inside BC modular arithmetic. */
    fun cancel() { cancelled.set(true) }
    private fun checkCancellation() { if (cancelled.get()) throw PairingProtocolException() }
    private fun guard() { checkCancellation(); if (state != PairingState.ACTIVE) throw PairingProtocolException() }
    private fun fail() { state=PairingState.FAILED; release() }
    private fun release() { code.fill('\u0000'); exchange?.close(); exchange=null; transcript.reset() }
    @Synchronized override fun close() { release(); state=PairingState.CLOSED }
    private fun u16(bytes: ByteArray,offset: Int) = ((bytes[offset].toInt() and 255) shl 8) or (bytes[offset+1].toInt() and 255)
    private fun sha(bytes: ByteArray) = MessageDigest.getInstance("SHA-256").digest(bytes)
}

/** PAKE-bound CA bytes, NOT yet an X.509 trust anchor. Does not authorize files. */
class PakeConfirmation internal constructor(ca: ByteArray,client: ByteArray,attempt: ByteArray,val httpsPort: Int,grant: ByteArray) : AutoCloseable {
    private val caBytes=ca.copyOf()
    private val clientBytes=client.copyOf()
    private val attemptBytes=attempt.copyOf()
    private val grantBytes=grant.copyOf()
    private var closed=false
    val candidateCaDer: ByteArray get() = caBytes.copyOf()
    val clientId: ByteArray get() = clientBytes.copyOf()
    val attemptId: ByteArray get() = attemptBytes.copyOf()
    @Synchronized fun copyGrant(): ByteArray { check(!closed); return grantBytes.copyOf() }
    override fun toString() = "PakeConfirmation(redacted)"
    @Synchronized override fun close() { grantBytes.fill(0); closed=true }
}
