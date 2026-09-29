package org.phonebridge.ng

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NotificationPermissionPolicyTests {
    @Test fun sharingRequestsNotificationPermissionOnlyWhenRequired() {
        assertFalse(NotificationPermissionPolicy.shouldRequest(ForegroundAction.START_SHARING, 32, false))
        assertFalse(NotificationPermissionPolicy.shouldRequest(ForegroundAction.START_SHARING, 36, true))
        assertTrue(NotificationPermissionPolicy.shouldRequest(ForegroundAction.START_SHARING, 36, false))
    }

    @Test fun pairingNeverWaitsForNotificationPermission() {
        assertFalse(NotificationPermissionPolicy.shouldRequest(ForegroundAction.OPEN_PAIRING, 32, false))
        assertFalse(NotificationPermissionPolicy.shouldRequest(ForegroundAction.OPEN_PAIRING, 36, false))
        assertFalse(NotificationPermissionPolicy.shouldRequest(ForegroundAction.OPEN_PAIRING, 36, true))
    }
}
