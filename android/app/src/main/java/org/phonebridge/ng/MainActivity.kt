package org.phonebridge.ng

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.provider.Settings
import android.view.WindowManager
import android.view.View
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import org.phonebridge.credentials.AccessMode
import org.phonebridge.credentials.ClientState

class MainActivity : Activity() {
    private val handler = Handler(Looper.getMainLooper())
    private var binder: SharingService.LocalBinder? = null
    private var bound = false
    private var visible = false
    private lateinit var content: LinearLayout
    private lateinit var status: TextView
    private lateinit var folder: Spinner
    private lateinit var pairingArea: LinearLayout
    private lateinit var clientsArea: LinearLayout
    private lateinit var start: Button
    private lateinit var pair: Button
    private var pendingStartFolder: Int? = null
    private var renderedPair: String? = null
    private var renderedClients: Any? = null
    private var countdown: TextView? = null
    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName, service: IBinder) { binder = service as SharingService.LocalBinder; if (visible) binder?.enterForeground(); render() }
        override fun onServiceDisconnected(name: ComponentName) { binder = null; render() }
    }
    private val refresh = object : Runnable { override fun run() { if (visible) { render(); handler.postDelayed(this, 500) } } }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        content = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(32, 24, 32, 24) }
        val scroll = ScrollView(this).apply { addView(content) }
        scroll.setOnApplyWindowInsetsListener { view, insets ->
            @Suppress("DEPRECATION")
            view.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom)
            insets
        }
        setContentView(scroll)
        label(content, getString(R.string.app_name), 28f)
        label(content, getString(R.string.subtitle))
        status = label(content, getString(R.string.stopped), 20f)
        label(content, getString(R.string.folder))
        folder = Spinner(this).apply {
            adapter = ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_dropdown_item, SharedFolders.names)
            setSelection(getSharedPreferences("settings", Context.MODE_PRIVATE).getInt("folder", 0).coerceIn(0, SharedFolders.names.lastIndex))
        }; content.addView(folder)
        button(content, R.string.permission) { requestStorage() }
        start = button(content, R.string.start) {
            if (binder?.service?.engine != null) startService(Intent(this, SharingService::class.java).setAction(SharingService.STOP))
            else if (!SharedFolders.allowed(this)) status.setText(R.string.permission_missing)
            else requestStart(folder.selectedItemPosition)
        }
        pair = button(content, R.string.pair) { binder?.openPairing() }
        pairingArea = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }; content.addView(pairingArea)
        label(content, getString(R.string.computers), 20f)
        clientsArea = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }; content.addView(clientsArea)
        label(content, getString(R.string.development_limit))
    }
    override fun onStart() { super.onStart(); bound = bindService(Intent(this, SharingService::class.java), connection, BIND_AUTO_CREATE) }
    override fun onResume() {
        super.onResume(); visible = true; binder?.enterForeground(); handler.post(refresh)
        pendingStartFolder?.let { selected ->
            pendingStartFolder = null
            if (BatteryOptimizationPolicy.isExempt(this)) beginSharing(selected)
            else status.setText(R.string.battery_optimization_required)
        }
    }
    override fun onPause() { visible = false; handler.removeCallbacks(refresh); binder?.leaveForeground(); super.onPause() }
    override fun onStop() { if (bound) { unbindService(connection); bound = false }; binder = null; super.onStop() }
    private fun render() {
        if (!::content.isInitialized) return
        val service = binder?.service; val engine = service?.engine
        status.setText(service?.status ?: R.string.stopped)
        start.setText(if (engine == null) R.string.start else R.string.stop)
        folder.isEnabled = engine == null; pair.isEnabled = engine != null
        val view = try { engine?.pairing?.view() } catch (_: Exception) { null }
        // Do not recreate approval controls on every timer tick: an exact pending attempt owns its buttons.
        val key = view?.let { "${it.windowId}:${it.attemptId}:${it.state}:${it.attemptsUsed}" }
        pair.isEnabled = engine != null && view == null
        if (key != renderedPair) {
            renderedPair = key; pairingArea.removeAllViews(); countdown = null
            if (view != null) {
                countdown = label(pairingArea, resources.getQuantityString(R.plurals.pair_remaining, view.remainingSeconds, view.remainingSeconds, view.attemptsUsed))
                if (view.code != null) { label(pairingArea, getString(R.string.pair_code)); label(pairingArea, view.code, 32f) }
                else label(pairingArea, getString(when (view.state) { "Active" -> R.string.paired; "Rejected" -> R.string.rejected; "Cancelled" -> R.string.cancelled; else -> R.string.pair_waiting }))
                if (view.pendingName != null && view.attemptId != null) {
                    label(pairingArea, getString(R.string.approve_prompt, view.pendingName)); label(pairingArea, getString(R.string.approve_note))
                    button(pairingArea, R.string.approve) { binder?.decide(view.attemptId, true) }
                    button(pairingArea, R.string.reject) { binder?.decide(view.attemptId, false) }
                }
                button(pairingArea, R.string.close_pairing) { binder?.closePairing(); render() }
            }
        }
        if (view != null) countdown?.text = resources.getQuantityString(R.plurals.pair_remaining, view.remainingSeconds, view.remainingSeconds, view.attemptsUsed)
        val clients = engine?.clients?.clients?.filter { it.state == ClientState.ACTIVE } ?: emptyList()
        if (renderedClients != clients) {
            renderedClients = clients; clientsArea.removeAllViews()
            if (clients.isEmpty()) label(clientsArea, getString(R.string.no_computers))
            for (client in clients) {
                label(clientsArea, client.clientName)
                label(clientsArea, getString(R.string.access_mode))
                val modes = listOf(getString(R.string.mode_read_only), getString(R.string.mode_safe), getString(R.string.mode_read_write))
                val selector = Spinner(this).apply {
                    adapter = ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_dropdown_item, modes)
                    setSelection(when (client.mode) { AccessMode.READ_ONLY -> 0; AccessMode.SAFE -> 1; AccessMode.READ_WRITE -> 2 })
                    onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
                        override fun onNothingSelected(parent: AdapterView<*>?) = Unit
                        override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) {
                            val selected = when (position) { 0 -> AccessMode.READ_ONLY; 1 -> AccessMode.SAFE; else -> AccessMode.READ_WRITE }
                            if (selected != client.mode) {
                                renderedClients = null
                                binder?.updateMode(client.clientId, selected)
                            }
                        }
                    }
                }
                clientsArea.addView(selector)
                button(clientsArea, R.string.revoke) {
                    AlertDialog.Builder(this).setMessage(getString(R.string.revoke_prompt, client.clientName))
                        .setPositiveButton(R.string.revoke) { _, _ -> binder?.revoke(client.clientId) }.setNegativeButton(R.string.cancel, null).show()
                }
            }
        }
    }
    private fun label(parent: LinearLayout, value: String, size: Float = 16f): TextView = TextView(this).apply {
        text = value; textSize = size; setPadding(0, 12, 0, 12); parent.addView(this)
    }
    private fun button(parent: LinearLayout, text: Int, click: () -> Unit): Button = Button(this).apply {
        setText(text); isAllCaps = false; setOnClickListener { click() }; parent.addView(this)
    }
    private fun requestStart(selected: Int) {
        if (BatteryOptimizationPolicy.isExempt(this)) { beginSharing(selected); return }
        pendingStartFolder = selected
        status.setText(R.string.battery_optimization_required)
        val direct = BatteryOptimizationPolicy.requestIntent(packageName)
        try {
            startActivity(direct)
        } catch (_: android.content.ActivityNotFoundException) {
            try { startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)) }
            catch (_: android.content.ActivityNotFoundException) {
                pendingStartFolder = null
                status.setText(R.string.battery_optimization_settings_failed)
            }
        }
    }
    private fun beginSharing(selected: Int) {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 2)
        startForegroundService(Intent(this, SharingService::class.java).setAction(SharingService.START).putExtra("folder", selected))
    }
    private fun requestStorage() {
        if (Build.VERSION.SDK_INT >= 30) startActivity(Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:$packageName")))
        else requestPermissions(arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE, Manifest.permission.WRITE_EXTERNAL_STORAGE), 1)
    }
}
