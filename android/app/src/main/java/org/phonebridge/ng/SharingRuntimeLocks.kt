package org.phonebridge.ng

import android.annotation.SuppressLint
import android.content.Context
import android.net.wifi.WifiManager
import android.os.PowerManager

/** Owns the exact power resources for one user-visible sharing lifetime. */
internal class SharingRuntimeLocks(private val context: Context) : AutoCloseable {
    private var multicast: WifiManager.MulticastLock? = null
    private var awake: PowerManager.WakeLock? = null
    val multicastHeld: Boolean get() = multicast?.isHeld == true
    val wakeHeld: Boolean get() = awake?.isHeld == true

    @SuppressLint("WakelockTimeout")
    fun acquire() {
        check(multicast == null && awake == null)
        val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        multicast = wifi.createMulticastLock("PhoneBridge-NG:mdns").apply {
            setReferenceCounted(false)
            acquire()
        }
        try {
            // Sharing is user-started and notification-visible. close() owns every release path.
            awake = context.getSystemService(PowerManager::class.java)
                .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "PhoneBridge-NG:sharing").apply {
                    setReferenceCounted(false)
                    acquire()
                }
        } catch (error: Exception) {
            close()
            throw error
        }
    }

    override fun close() {
        if (awake?.isHeld == true) awake?.release()
        awake = null
        if (multicast?.isHeld == true) multicast?.release()
        multicast = null
    }
}
