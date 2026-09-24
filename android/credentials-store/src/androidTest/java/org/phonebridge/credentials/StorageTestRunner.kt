package org.phonebridge.credentials

import android.app.Activity
import android.os.Bundle
import android.os.Process
import androidx.test.runner.AndroidJUnitRunner

/** Synthetic isolated test host, never packaged into the library AAR. */
class StorageTestRunner : AndroidJUnitRunner() {
    private var argumentsCopy: Bundle? = null
    override fun onCreate(arguments: Bundle) { argumentsCopy = Bundle(arguments); super.onCreate(arguments) }
    override fun onStart() {
        val args = argumentsCopy ?: Bundle()
        val action = args.getString("fixture")
        if (action == null) { super.onStart(); return }
        try {
            val name = args.getString("store") ?: error("fixture")
            require(name.matches(Regex("restart-[a-z0-9-]{1,48}")))
            val ca = "11".repeat(32); val client = "22".repeat(16); val token = ByteArray(32) { (it + 1).toByte() }
            val crash = if (action == "crash") CommitStage.valueOf(args.getString("stage") ?: error("fixture")) else null
            val store = PairingStore.testStore(targetContext, ca, name) {
                if (it == crash) {
                    sendStatus(3, Bundle().apply { putString("checkpoint", it.name); putInt("fixture_pid", Process.myPid()) })
                    Process.killProcess(Process.myPid())
                    throw IllegalStateException("process termination returned")
                }
            }
            if (action == "prepare") { store.initialize(); store.approve(client, "synthetic pc", token, 1) }
            if (action == "crash" || action == "revoke") { store.revoke(client, store.snapshot().revision) }
            val snapshot = store.snapshot(); val state = snapshot.clients.single().state
            if (state == ClientState.ACTIVE) store.authenticate(client, token)
            else {
                try { store.authenticate(client, token); error("revoked accepted") }
                catch (e: StoreException) { check(e.error == StoreError.UNAUTHORIZED) }
            }
            val key = store.keyProtection(); token.fill(0)
            finish(Activity.RESULT_OK, Bundle().apply {
                putString("fixture_state", state.name); putLong("revision", snapshot.revision); putInt("fixture_pid", Process.myPid())
                putString("security_level", key.securityLevel?.toString() ?: "api_unavailable")
                putBoolean("inside_secure_hardware", key.insideSecureHardware)
            })
        } catch (_: Exception) { finish(Activity.RESULT_CANCELED, Bundle().apply { putString("fixture_error", "failed") }) }
    }
}
