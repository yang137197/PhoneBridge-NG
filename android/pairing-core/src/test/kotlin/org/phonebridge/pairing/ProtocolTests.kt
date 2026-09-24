package org.phonebridge.pairing

import org.junit.Assert.*
import org.junit.Test

class ProtocolTests {
    private val window=ByteArray(16) { it.toByte() }
    private fun w(code: String="00123456") = PairingSession.createWindows(window,code.toCharArray())
    private fun a(code: String="00123456") = PairingSession.createAndroid(window,code.toCharArray(),8273,byteArrayOf(0x30,0))
    private fun exchange(w: PairingSession,a: PairingSession,count: Int=8) {
        repeat(count) { i -> (if (i%2==0) a else w).acceptFrame((if (i%2==0) w else a).createNextFrame()) }
    }
    private fun reject(block: () -> Unit) {
        try { block(); fail("Expected protocol rejection") }
        catch (e: PairingProtocolException) { assertEquals("pairing_protocol_error",e.message); assertNull(e.cause) }
    }

    @Test fun fullHandshakeAndGrantOwnership() {
        for (code in listOf("00123456","00000000")) w(code).use { w -> a(code).use { a ->
            exchange(w,a)
            w.takeConfirmation().use { x -> a.takeConfirmation().use { y ->
                assertArrayEquals(x.copyGrant(),y.copyGrant()); assertArrayEquals(x.clientId,y.clientId)
                assertArrayEquals(x.attemptId,y.attemptId); assertArrayEquals(byteArrayOf(0x30,0),x.candidateCaDer)
                assertEquals(8273,x.httpsPort); val copy=x.copyGrant(); copy[0]=(copy[0].toInt() xor 255).toByte()
                assertFalse(copy.contentEquals(x.copyGrant())); assertEquals(PairingState.CONSUMED,w.state)
                x.close(); assertThrows(IllegalStateException::class.java) { x.copyGrant() }
                reject { w.takeConfirmation() }
            } }
        } }
    }
    @Test fun wrongCodeAndEarlyResult() {
        w().use { w -> a("00123457").use { a -> reject { exchange(w,a) }; reject { a.takeConfirmation() } } }
        for (steps in listOf(0,1,3,6,7)) w().use { w -> a().use { a ->
            exchange(w,a,steps); reject { w.takeConfirmation() }; assertEquals(PairingState.FAILED,w.state)
        } }
    }
    @Test fun directionCancellationAndReuse() {
        a().use { a -> reject { a.createNextFrame() }; reject { a.acceptFrame(PairingFrames.encode(1,ByteArray(33))) } }
        w().use { w -> w.cancel(); reject { w.createNextFrame() }; assertEquals(PairingState.FAILED,w.state) }
    }
    @Test fun allFrameTypesAcceptFragments() {
        for ((type,size) in listOf(1 to 33,2 to 54,2 to 4149,17 to 1600,18 to 1600,33 to 800,34 to 800,49 to 32,50 to 32)) {
            val frame=PairingFrames.encode(type,ByteArray(size));val reader=FrameAccumulator(type)
            frame.forEach { reader.feed(byteArrayOf(it)) }; assertTrue(reader.isComplete);assertArrayEquals(frame,reader.getFrame())
            reject { reader.feed(byteArrayOf(0)) };assertFalse(reader.isComplete)
        }
    }
    @Test fun incompleteAndOversizedFrames() {
        for (size in listOf(0,4,8,9,20)) {
            val reader=FrameAccumulator(1);reader.feed(PairingFrames.encode(1,ByteArray(33)).copyOf(size))
            reject { reader.endOfInput() };reject { reader.feed(byteArrayOf(0)) }
        }
        for (size in listOf(0,32,34,8193,-1)) {
            val header=byteArrayOf(80,66,80,49,1,0,0,0,0)
            repeat(4) { header[5+it]=(size ushr (24-8*it)).toByte() }
            reject { FrameAccumulator(1).feed(header) }
        }
    }
    @Test fun magicTypeTrailingAndDuplicate() {
        val frame=PairingFrames.encode(1,ByteArray(33));val bad=frame.copyOf();bad[0]=0
        reject { FrameAccumulator(1).feed(bad) };reject { FrameAccumulator(2).feed(frame) }
        reject { FrameAccumulator(1).feed(frame+byteArrayOf(0)) }
        w().use { w -> a().use { a -> val hello=w.createNextFrame();a.acceptFrame(hello);reject { a.acceptFrame(hello) } } }
    }
    @Test fun invalidHelloFields() {
        for (offset in listOf(9,10,26,60,61)) w().use { w -> a().use { a ->
            exchange(w,a,1); val hello=a.createNextFrame();hello[offset]=(hello[offset].toInt() xor 1).toByte()
            reject { w.acceptFrame(hello) }
        } }
    }
    @Test fun boundHelloChangesInvalidateProof() {
        for (offset in listOf(42,58,62)) w().use { w -> a().use { a ->
            exchange(w,a,1);val hello=a.createNextFrame();hello[offset]=(hello[offset].toInt() xor 1).toByte()
            w.acceptFrame(hello);reject { a.acceptFrame(w.createNextFrame()) }
        } }
    }
    @Test fun reflectionAndReplay() {
        w().use { w -> a().use { a ->
            exchange(w,a,2);val r1=w.createNextFrame();a.acceptFrame(r1);val old=a.createNextFrame()
            val reflection=r1.copyOf();reflection[4]=0x12;reject { w.acceptFrame(reflection) }
            w().use { w2 -> a().use { a2 -> exchange(w2,a2,2);w2.createNextFrame();reject { w2.acceptFrame(old) } } }
        } }
    }
    @Test fun groupAndScalarBounds() {
        for (kind in 0..3) w().use { w -> a().use { a ->
            exchange(w,a,2);val r1=w.createNextFrame()
            if (kind<3) { r1.fill(0,393,777);if(kind==1)r1[776]=1;if(kind==2)r1.fill(-1,393,777) }
            else r1.fill(-1,1161,1193)
            reject { a.acceptFrame(r1) };assertEquals(PairingState.FAILED,a.state)
        } }
    }
    @Test fun modifiedRound2AndRound3() {
        for(step in 4..7) w().use { w -> a().use { a ->
            exchange(w,a,step);val sender=if(step%2==0)w else a;val receiver=if(step%2==0)a else w
            val frame=sender.createNextFrame();frame[frame.lastIndex]=(frame.last().toInt() xor 1).toByte()
            reject { receiver.acceptFrame(frame) }
        } }
    }
    @Test fun invalidInputsAndClosedSession() {
        reject { w("１２３４５６７８") }
        reject { PairingSession.createAndroid(window,"00123456".toCharArray(),0,byteArrayOf(1)) }
        reject { PairingSession.createAndroid(window,"00123456".toCharArray(),1,ByteArray(4097)) }
        w().use { w -> w.close();reject { w.createNextFrame() } }
    }
    @Test fun hkdfRfc5869KnownAnswer() {
        val value=PairingSession.hkdf(ByteArray(22) { 0x0b },ByteArray(13) { it.toByte() },ByteArray(10) { (0xf0+it).toByte() },42)
        assertEquals("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865",value.joinToString("") { "%02x".format(it.toInt() and 255) })
    }
    @Test fun extraFrameAfterConfirmationInvalidatesUntakenResult() {
        w().use { w -> a().use { a -> exchange(w,a)
            reject { w.acceptFrame(PairingFrames.encode(0x32,ByteArray(32))) };reject { w.takeConfirmation() }
        } }
    }
    @Test fun zeroServerPortRejected() {
        w().use { w -> a().use { a -> exchange(w,a,1);val frame=a.createNextFrame();frame[58]=0;frame[59]=0
            reject { w.acceptFrame(frame) }
        } }
    }
}
