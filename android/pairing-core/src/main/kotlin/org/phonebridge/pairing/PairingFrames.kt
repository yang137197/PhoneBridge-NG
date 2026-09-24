package org.phonebridge.pairing

class PairingProtocolException : Exception("pairing_protocol_error")

object PairingFrames {
    const val HEADER_SIZE = 9
    const val MAX_PAYLOAD = 8192
    const val MAX_SESSION_BYTES = 32768
    internal val order = intArrayOf(1, 2, 0x11, 0x12, 0x21, 0x22, 0x31, 0x32)

    internal fun length(header: ByteArray, expected: Int): Int {
        if (header.size != HEADER_SIZE || !header.copyOfRange(0,4).contentEquals(byteArrayOf(80,66,80,49)) ||
            header[4].toInt() and 255 != expected) throw PairingProtocolException()
        var size = 0L
        for (i in 5..8) size = (size shl 8) or (header[i].toLong() and 255)
        val valid = when (expected) {
            1 -> size == 33L
            2 -> size in 54L..4149L
            0x11, 0x12 -> size == 1600L
            0x21, 0x22 -> size == 800L
            0x31, 0x32 -> size == 32L
            else -> false
        }
        if (!valid || size > MAX_PAYLOAD) throw PairingProtocolException()
        return size.toInt()
    }

    fun validate(frame: ByteArray, expected: Int) {
        if (frame.size < HEADER_SIZE || length(frame.copyOfRange(0,HEADER_SIZE),expected) != frame.size-HEADER_SIZE)
            throw PairingProtocolException()
    }

    internal fun encode(type: Int, body: ByteArray): ByteArray {
        val out = ByteArray(HEADER_SIZE+body.size)
        byteArrayOf(80,66,80,49,type.toByte()).copyInto(out)
        for (i in 0..3) out[5+i] = (body.size ushr (24-8*i)).toByte()
        body.copyInto(out,HEADER_SIZE); validate(out,type); return out
    }
}

/** One expected frame. The transport must check EOF and reject surplus bytes. */
class FrameAccumulator(private val expected: Int) {
    private val header = ByteArray(PairingFrames.HEADER_SIZE)
    private var frame: ByteArray? = null
    private var count = 0
    private var failed = false
    val isComplete: Boolean get() = !failed && frame != null && count == frame!!.size
    init { if (!PairingFrames.order.contains(expected)) throw PairingProtocolException() }

    fun feed(bytes: ByteArray) {
        try {
            if (failed || isComplete) throw PairingProtocolException()
            var offset = 0
            if (count < header.size) {
                val n = minOf(bytes.size,header.size-count)
                bytes.copyInto(header,count,0,n); count += n; offset += n
                if (count < header.size) return
                frame = ByteArray(header.size+PairingFrames.length(header,expected))
                header.copyInto(frame!!)
            }
            if (bytes.size-offset > frame!!.size-count) throw PairingProtocolException()
            bytes.copyInto(frame!!,count,offset); count += bytes.size-offset
        } catch (_: Exception) { failed = true; frame = null; throw PairingProtocolException() }
    }

    fun endOfInput() {
        if (!isComplete) { failed = true; frame = null; throw PairingProtocolException() }
    }
    fun getFrame(): ByteArray { endOfInput(); return frame!!.copyOf() }
}
