package com.phonebridge.server

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class XmlResponseBuilderTests {
    @Test fun rootQuotaUsesAvailableAndTotalMinusAvailableBytes() {
        val root = SharedEntry(SharedPath.parse("/"), true, 0, 0)
        val child = SharedEntry(SharedPath.parse("/Download"), true, 0, 0)

        val xml = XmlResponseBuilder.buildPropfindResponse(
            listOf(root, child), StorageQuota(totalBytes = 1_000, availableBytes = 400))

        assertTrue(xml.contains("<D:quota-available-bytes>400</D:quota-available-bytes>"))
        assertTrue(xml.contains("<D:quota-used-bytes>600</D:quota-used-bytes>"))
        assertEquals(1, Regex("<D:quota-available-bytes>").findAll(xml).count())
        assertEquals(1, Regex("<D:quota-used-bytes>").findAll(xml).count())
    }
}
