package org.phonebridge.ng

internal enum class ForegroundAction { START_SHARING, OPEN_PAIRING }

internal object NotificationPermissionPolicy {
    fun shouldRequest(action: ForegroundAction, sdk: Int, granted: Boolean): Boolean =
        action == ForegroundAction.START_SHARING && sdk >= 33 && !granted
}
