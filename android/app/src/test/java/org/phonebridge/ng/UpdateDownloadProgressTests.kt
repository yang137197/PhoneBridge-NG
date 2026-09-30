package org.phonebridge.ng

import org.junit.Assert.assertEquals
import org.junit.Test

class UpdateDownloadProgressTests {
    @Test fun reportsBoundedPercentage() {
        assertEquals(0, UpdateDownloadProgress(0, 100).percent)
        assertEquals(50, UpdateDownloadProgress(50, 100).percent)
        assertEquals(99, UpdateDownloadProgress(99, 100).percent)
        assertEquals(100, UpdateDownloadProgress(100, 100).percent)
        assertEquals(100, UpdateDownloadProgress(120, 100).percent)
    }

    @Test fun invalidTotalIsSafe() {
        assertEquals(0, UpdateDownloadProgress(10, 0).percent)
        assertEquals(0, UpdateDownloadProgress(10, -1).percent)
    }
}
