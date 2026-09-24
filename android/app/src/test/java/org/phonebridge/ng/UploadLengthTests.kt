package org.phonebridge.ng

import org.junit.Assert.assertEquals
import org.junit.Assert.fail
import org.junit.Test

class UploadLengthTests {
    @Test fun acceptanceSizesUseDecimalBytes() {
        assertEquals(10_000_000_000L, validatedBodyLength(mapOf("content-length" to "10000000000"), MAX_UPLOAD_BYTES))
        assertEquals(20_000_000_000L, validatedBodyLength(mapOf("content-length" to "20000000000"), MAX_UPLOAD_BYTES))
    }

    @Test fun sizeAboveAcceptanceLimitFailsClosed() {
        try {
            validatedBodyLength(mapOf("content-length" to "20000000001"), MAX_UPLOAD_BYTES)
            fail("oversized upload accepted")
        } catch (error: ApiFailure) {
            assertEquals(400, error.status)
            assertEquals("invalid_request", error.code)
        }
    }
}
