package org.phonebridge.pairing

import java.math.BigInteger
import java.security.SecureRandom
import org.bouncycastle.crypto.agreement.jpake.*
import org.bouncycastle.crypto.digests.SHA256Digest

internal class PakeExchange(private val own: String, private val peer: String, code: CharArray) : AutoCloseable {
    private var participant: JPAKEParticipant? = JPAKEParticipant(own,code,JPAKEPrimeOrderGroups.NIST_3072,SHA256Digest(),SecureRandom())
    private var first: JPAKERound1Payload? = null
    private var second: JPAKERound2Payload? = null
    private var third: JPAKERound3Payload? = null
    private var key: BigInteger? = null

    fun round1(): ByteArray {
        if (first == null) first = participant!!.createRound1PayloadToSend()
        val p = first!!
        return u(p.gx1,384)+u(p.gx2,384)+u(p.knowledgeProofForX1[0],384)+u(p.knowledgeProofForX1[1],32)+
            u(p.knowledgeProofForX2[0],384)+u(p.knowledgeProofForX2[1],32)
    }
    fun acceptRound1(b: ByteArray) {
        round1()
        participant!!.validateRound1PayloadReceived(JPAKERound1Payload(peer,element(b,0),element(b,384,true),
            arrayOf(element(b,768),scalar(b,1152)),arrayOf(element(b,1184),scalar(b,1568))))
    }
    fun round2(): ByteArray {
        if (second == null) second = participant!!.createRound2PayloadToSend()
        val p = second!!
        return u(p.a,384)+u(p.knowledgeProofForX2s[0],384)+u(p.knowledgeProofForX2s[1],32)
    }
    fun acceptRound2(b: ByteArray) {
        round2()
        participant!!.validateRound2PayloadReceived(JPAKERound2Payload(peer,element(b,0,true),arrayOf(element(b,384),scalar(b,768))))
    }
    fun round3(): ByteArray {
        if (key == null) key = participant!!.calculateKeyingMaterial()
        if (third == null) third = participant!!.createRound3PayloadToSend(key!!)
        return signed(third!!.macTag)
    }
    fun acceptRound3(b: ByteArray) {
        round3()
        // Pinned BC Java 1.86 validates the fixed-width MAC in constant time.
        participant!!.validateRound3PayloadReceived(JPAKERound3Payload(peer,BigInteger(b)),key!!)
    }
    fun exportKey(): ByteArray = u(key ?: throw PairingProtocolException(),384)

    private fun element(b: ByteArray, offset: Int, notOne: Boolean = false): BigInteger {
        val v = BigInteger(1,b.copyOfRange(offset,offset+384))
        if (v.signum() <= 0 || v >= JPAKEPrimeOrderGroups.NIST_3072.p || (notOne && v == BigInteger.ONE))
            throw PairingProtocolException()
        return v
    }
    private fun scalar(b: ByteArray, offset: Int): BigInteger {
        val v = BigInteger(1,b.copyOfRange(offset,offset+32))
        if (v >= JPAKEPrimeOrderGroups.NIST_3072.q) throw PairingProtocolException()
        return v
    }
    private fun u(v: BigInteger, size: Int): ByteArray {
        val bytes = v.toByteArray()
        val offset = if (bytes.size > 1 && bytes[0] == 0.toByte()) 1 else 0
        if (v.signum() < 0 || bytes.size-offset > size) throw PairingProtocolException()
        val out = ByteArray(size); bytes.copyInto(out,size-bytes.size+offset,offset); bytes.fill(0); return out
    }
    private fun signed(v: BigInteger): ByteArray {
        val bytes = v.toByteArray()
        if (bytes.size > 32) throw PairingProtocolException()
        val out = ByteArray(32) { if (v.signum() < 0) (-1).toByte() else 0 }
        bytes.copyInto(out,32-bytes.size); return out
    }
    override fun close() {
        // The library has no abort/zeroize API. Failed instances are never reused.
        participant = null; first = null; second = null; third = null; key = null
    }
}
