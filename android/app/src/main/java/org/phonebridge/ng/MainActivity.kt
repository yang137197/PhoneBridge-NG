package org.phonebridge.ng

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.content.pm.PackageManager
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.provider.Settings
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import org.phonebridge.credentials.AccessMode
import org.phonebridge.credentials.ClientState

class MainActivity : Activity() {
    private enum class Screen { HOME, PAIRING, CLIENT, SETTINGS, LANGUAGE }

    private val canvas = Color.rgb(244, 247, 251)
    private val surface = Color.WHITE
    private val subtle = Color.rgb(237, 244, 252)
    private val strong = Color.rgb(23, 32, 51)
    private val text = Color.rgb(53, 65, 84)
    private val muted = Color.rgb(102, 116, 135)
    private val border = Color.rgb(216, 225, 236)
    private val primary = Color.rgb(23, 105, 224)
    private val connectionColor = Color.rgb(16, 157, 168)
    private val success = Color.rgb(22, 134, 91)
    private val danger = Color.rgb(196, 61, 75)

    private val handler = Handler(Looper.getMainLooper())
    private var binder: SharingService.LocalBinder? = null
    private var bound = false
    private var visible = false
    private lateinit var content: LinearLayout
    private lateinit var status: TextView
    private lateinit var folder: Spinner
    private lateinit var clientsArea: LinearLayout
    private lateinit var start: Button
    private lateinit var pair: Button
    private lateinit var pairingArea: LinearLayout
    private var pendingStartFolder: Int? = null
    private var renderedClients: Any? = null
    private var countdown: TextView? = null
    private var screen = Screen.HOME
    private var selectedClientId: String? = null

    private val connection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName, service: IBinder) {
            binder = service as SharingService.LocalBinder
            if (visible) binder?.enterForeground()
            render()
        }
        override fun onServiceDisconnected(name: ComponentName) { binder = null; render() }
    }
    private val refresh = object : Runnable {
        override fun run() {
            if (visible) { render(); handler.postDelayed(this, 500) }
        }
    }

    override fun attachBaseContext(newBase: Context) { super.attachBaseContext(AppLanguage.wrap(newBase)) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!resources.getBoolean(R.bool.allow_screenshots)) window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(18), dp(16), dp(32))
            setBackgroundColor(canvas)
        }
        val scroll = ScrollView(this).apply {
            isFillViewport = true
            addView(content, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
        scroll.setOnApplyWindowInsetsListener { view, insets ->
            @Suppress("DEPRECATION")
            view.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom)
            insets
        }
        setContentView(scroll)
        when (savedInstanceState?.getString("screen")) {
            Screen.SETTINGS.name -> showSettings()
            Screen.LANGUAGE.name -> showLanguageSettings()
            else -> showHome()
        }
    }

    override fun onSaveInstanceState(outState: Bundle) { outState.putString("screen", screen.name); super.onSaveInstanceState(outState) }

    override fun onStart() { super.onStart(); bound = bindService(Intent(this, SharingService::class.java), connection, BIND_AUTO_CREATE) }
    override fun onResume() {
        super.onResume(); visible = true; binder?.enterForeground(); handler.post(refresh)
        pendingStartFolder?.let { selected ->
            pendingStartFolder = null
            if (BatteryOptimizationPolicy.isExempt(this)) beginSharing(selected)
            else if (screen == Screen.HOME) status.setText(R.string.battery_optimization_required)
        }
    }
    override fun onPause() { visible = false; handler.removeCallbacks(refresh); binder?.leaveForeground(); super.onPause() }
    override fun onStop() { if (bound) { unbindService(connection); bound = false }; binder = null; super.onStop() }

    private fun showHome() {
        screen = Screen.HOME
        content.removeAllViews()
        renderedClients = null

        val top = row(content)
        label(top, getString(R.string.app_name), 28f, true).apply {
            layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
        }
        iconButton(top, R.drawable.ic_settings, R.string.settings) { showSettings() }

        val statusCard = card(content, Color.rgb(229, 248, 249))
        val statusRow = row(statusCard)
        label(statusRow, "⌁", 30f, true, connectionColor).apply {
            gravity = Gravity.CENTER
            background = rounded(Color.rgb(207, 243, 246), dp(24).toFloat())
            layoutParams = LinearLayout.LayoutParams(dp(52), dp(52)).apply { marginEnd = dp(12) }
        }
        val statusText = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        statusRow.addView(statusText, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        status = label(statusText, getString(R.string.stopped), 18f, true, success)
        label(statusText, getString(R.string.subtitle), 13f, false, muted)

        val folderCard = card(statusCard, surface, compact = true)
        label(folderCard, getString(R.string.folder), 12f, true, muted)
        folder = Spinner(this).apply {
            adapter = ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_dropdown_item, SharedFolders.names)
            setSelection(getSharedPreferences("settings", Context.MODE_PRIVATE).getInt("folder", 0).coerceIn(0, SharedFolders.names.lastIndex))
        }
        folderCard.addView(folder, matchWrap(top = 0))

        if (!SharedFolders.allowed(this)) {
            val permissionCard = card(content, subtle)
            label(permissionCard, getString(R.string.permission_card_title), 16f, true)
            label(permissionCard, getString(R.string.permission_missing), 13f, false, muted)
            button(permissionCard, R.string.permission, primary = false) { requestStorage() }
        }

        start = button(statusCard, R.string.start, primary = true) {
            if (binder?.service?.engine != null) startService(Intent(this, SharingService::class.java).setAction(SharingService.STOP))
            else if (!SharedFolders.allowed(this)) status.setText(R.string.permission_missing)
            else requestStart(folder.selectedItemPosition)
        }

        pair = button(content, R.string.pair, primary = false) {
            binder?.openPairing()
            showPairing()
        }
        sectionTitle(content, R.string.computers)
        clientsArea = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        content.addView(clientsArea, matchWrap())
        label(content, getString(R.string.development_limit), 12f, false, muted).apply {
            setPadding(dp(4), dp(14), dp(4), 0)
        }
        renderHome()
    }

    private fun showPairing() {
        screen = Screen.PAIRING
        content.removeAllViews()
        topBar(R.string.pair) { binder?.closePairing(); showHome() }
        val hero = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; gravity = Gravity.CENTER_HORIZONTAL }
        content.addView(hero, matchWrap(top = 18, bottom = 12))
        label(hero, "⌁", 48f, true, connectionColor).apply {
            gravity = Gravity.CENTER
            background = rounded(Color.rgb(220, 247, 249), dp(46).toFloat())
            layoutParams = LinearLayout.LayoutParams(dp(92), dp(92))
        }
        label(hero, getString(R.string.pair_code), 17f, true).gravity = Gravity.CENTER
        pairingArea = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        content.addView(pairingArea, matchWrap(top = 12))
        renderPairing()
    }

    private fun showClient(clientId: String) {
        selectedClientId = clientId
        screen = Screen.CLIENT
        content.removeAllViews()
        topBar(R.string.computer_details) { showHome() }
        renderClient()
    }

    private fun showSettings() {
        screen = Screen.SETTINGS
        content.removeAllViews()
        topBar(R.string.settings) { showHome() }

        sectionTitle(content, R.string.settings_general)
        menuItem(
            content,
            R.drawable.ic_language,
            R.string.language,
            getString(if (AppLanguage.selected(this) == AppLanguage.CHINESE) R.string.language_zh else R.string.language_en),
        ) { showLanguageSettings() }
        label(content, getString(R.string.no_theme), 12f, false, muted).setPadding(dp(4), dp(18), 0, 0)
    }

    private fun showLanguageSettings() {
        screen = Screen.LANGUAGE
        content.removeAllViews()
        topBar(R.string.language) { showSettings() }
        val language = card(content, surface)
        label(language, getString(R.string.language), 18f, true)
        val choices = RadioGroup(this).apply { orientation = RadioGroup.VERTICAL }
        val selectedLanguage = AppLanguage.selected(this)
        val chinese = RadioButton(this).apply { text = getString(R.string.language_zh); textSize = 15f; isChecked = selectedLanguage == AppLanguage.CHINESE }
        val english = RadioButton(this).apply { setText(R.string.language_en); textSize = 15f; isChecked = selectedLanguage == AppLanguage.ENGLISH }
        choices.addView(chinese, matchWrap(top = 12)); choices.addView(english, matchWrap(top = 6))
        chinese.setOnClickListener { if (AppLanguage.set(this, AppLanguage.CHINESE)) recreate() }
        english.setOnClickListener { if (AppLanguage.set(this, AppLanguage.ENGLISH)) recreate() }
        language.addView(choices, matchWrap())
        val note = card(content, subtle)
        label(note, getString(R.string.language_behavior), 16f, true)
        label(note, getString(R.string.language_note), 13f, false, muted)
    }

    private fun render() {
        when (screen) {
            Screen.HOME -> renderHome()
            Screen.PAIRING -> renderPairing()
            Screen.CLIENT -> renderClient()
            Screen.SETTINGS -> Unit
            Screen.LANGUAGE -> Unit
        }
    }

    private fun renderHome() {
        if (!::status.isInitialized || screen != Screen.HOME) return
        val service = binder?.service
        val engine = service?.engine
        status.setText(service?.status ?: R.string.stopped)
        status.setTextColor(if (engine == null) muted else success)
        start.setText(if (engine == null) R.string.start else R.string.stop)
        folder.isEnabled = engine == null
        pair.isEnabled = engine != null && runCatching { engine.pairing.view() }.getOrNull() == null
        val clients = engine?.clients?.clients?.filter { it.state == ClientState.ACTIVE } ?: emptyList()
        if (renderedClients == clients) return
        renderedClients = clients
        clientsArea.removeAllViews()
        if (clients.isEmpty()) {
            val empty = card(clientsArea, surface)
            label(empty, getString(R.string.no_computers), 14f, false, muted)
        }
        for (client in clients) {
            val item = card(clientsArea, surface, compact = true)
            item.isClickable = true
            item.isFocusable = true
            item.setOnClickListener { showClient(client.clientId) }
            val title = row(item)
            label(title, "▣", 20f, true, primary).apply { layoutParams = LinearLayout.LayoutParams(dp(36), dp(36)).apply { marginEnd = dp(10) }; gravity = Gravity.CENTER; background = rounded(subtle, dp(10).toFloat()) }
            val copy = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
            title.addView(copy, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            label(copy, client.clientName, 15f, true)
            label(copy, "●  ${getString(R.string.online)}", 12f, false, success)
            label(item, modeName(client.mode), 13f, false, text).setPadding(dp(46), dp(6), 0, 0)
        }
    }

    private fun renderPairing() {
        if (screen != Screen.PAIRING || !::pairingArea.isInitialized) return
        val view = try { binder?.service?.engine?.pairing?.view() } catch (_: Exception) { null }
        if (view?.state == "Active") { showHome(); return }
        pairingArea.removeAllViews()
        countdown = null
        if (view == null) {
            val waiting = card(pairingArea, surface)
            label(waiting, getString(R.string.pair_waiting), 16f, true)
            label(waiting, getString(R.string.action_rejected), 13f, false, muted)
            return
        }
        if (view.code != null) {
            val codeCard = card(pairingArea, subtle)
            label(codeCard, view.code.chunked(4).joinToString(" "), 34f, true, primary).apply { gravity = Gravity.CENTER; typeface = Typeface.MONOSPACE }
            countdown = label(codeCard, resources.getQuantityString(R.plurals.pair_remaining, view.remainingSeconds, view.remainingSeconds, view.attemptsUsed), 12f, false, muted).apply { gravity = Gravity.CENTER }
        }
        else {
            val stateText = when (view.state) { "Active" -> R.string.paired; "Rejected" -> R.string.rejected; "Cancelled" -> R.string.cancelled; else -> R.string.pair_waiting }
            label(pairingArea, getString(stateText), 16f, true)
        }
        val waitCard = card(pairingArea, surface)
        label(waitCard, getString(if (view.pendingName == null) R.string.pair_waiting else R.string.approval_status), 16f, true)
        if (view.pendingName != null && view.attemptId != null) {
            label(waitCard, getString(R.string.approve_prompt, view.pendingName), 14f, false, text)
            label(waitCard, getString(R.string.approve_note), 12f, false, muted)
            val actions = row(waitCard)
            button(actions, R.string.reject, primary = false) { binder?.decide(view.attemptId, false) }.apply { setTextColor(danger) }
            button(actions, R.string.approve, primary = true) { binder?.decide(view.attemptId, true) }
        }
        button(pairingArea, R.string.close_pairing, primary = false) { binder?.closePairing(); showHome() }
    }

    private fun renderClient() {
        if (screen != Screen.CLIENT) return
        val client = binder?.service?.engine?.clients?.clients?.firstOrNull { it.clientId == selectedClientId && it.state == ClientState.ACTIVE }
        if (client == null) { showHome(); return }
        while (content.childCount > 1) content.removeViewAt(content.childCount - 1)
        val identity = card(content, surface)
        label(identity, client.clientName, 17f, true)
        label(identity, "●  ${getString(R.string.online)}", 13f, false, success)

        val access = card(content, surface)
        label(access, getString(R.string.access_mode), 18f, true)
        val modes = RadioGroup(this).apply { orientation = RadioGroup.VERTICAL }
        val options = listOf(AccessMode.READ_ONLY to R.string.mode_read_only, AccessMode.SAFE to R.string.mode_safe, AccessMode.READ_WRITE to R.string.mode_read_write)
        for ((mode, label) in options) {
            val choice = RadioButton(this).apply { setText(label); textSize = 14f; isChecked = client.mode == mode; setPadding(0, dp(8), 0, dp(8)) }
            choice.setOnClickListener { if (client.mode != mode) { renderedClients = null; binder?.updateMode(client.clientId, mode) } }
            modes.addView(choice, matchWrap())
        }
        access.addView(modes, matchWrap(top = 8))
        label(access, getString(R.string.mode_reconnect), 12f, false, muted).apply { background = rounded(subtle, dp(8).toFloat()); setPadding(dp(12), dp(10), dp(12), dp(10)) }

        sectionTitle(content, R.string.danger_zone, danger)
        button(content, R.string.revoke, primary = false) {
            AlertDialog.Builder(this).setMessage(getString(R.string.revoke_prompt, client.clientName))
                .setPositiveButton(R.string.revoke) { _, _ -> binder?.revoke(client.clientId); showHome() }
                .setNegativeButton(R.string.cancel, null).show()
        }.apply { setTextColor(danger); background = outlined(danger) }
    }

    private fun topBar(title: Int, back: () -> Unit) {
        val top = row(content)
        iconButton(top, R.drawable.ic_back, R.string.back) { back() }
        label(top, getString(title), 24f, true).apply { setPadding(dp(12), 0, 0, 0) }
    }

    private fun sectionTitle(parent: LinearLayout, text: Int, color: Int = strong) = label(parent, getString(text), 18f, true, color).apply { setPadding(dp(2), dp(20), 0, dp(8)) }

    private fun card(parent: LinearLayout, color: Int, compact: Boolean = false): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        setPadding(dp(if (compact) 14 else 16), dp(if (compact) 13 else 16), dp(if (compact) 14 else 16), dp(if (compact) 13 else 16))
        background = rounded(color, dp(16).toFloat(), border)
        parent.addView(this, matchWrap(top = 12))
    }

    private fun row(parent: LinearLayout): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL
        gravity = Gravity.CENTER_VERTICAL
        parent.addView(this, matchWrap())
    }

    private fun label(parent: LinearLayout, value: String, size: Float = 16f, bold: Boolean = false, color: Int = text): TextView = TextView(this).apply {
        text = value
        textSize = size
        setTextColor(color)
        if (bold) setTypeface(typeface, Typeface.BOLD)
        setPadding(0, dp(5), 0, dp(5))
        parent.addView(this, matchWrap())
    }

    private fun button(parent: LinearLayout, textResource: Int, primary: Boolean, compact: Boolean = false, click: () -> Unit): Button = Button(this).apply {
        setText(textResource)
        isAllCaps = false
        textSize = 14f
        minHeight = dp(if (compact) 44 else 48)
        setPadding(dp(if (compact) 12 else 16), 0, dp(if (compact) 12 else 16), 0)
        setTextColor(if (primary) Color.WHITE else this@MainActivity.text)
        backgroundTintList = ColorStateList.valueOf(if (primary) this@MainActivity.primary else surface)
        if (!primary) background = outlined(border)
        setOnClickListener { click() }
        val horizontal = parent.orientation == LinearLayout.HORIZONTAL
        val width = if (!horizontal) ViewGroup.LayoutParams.MATCH_PARENT else if (compact) ViewGroup.LayoutParams.WRAP_CONTENT else 0
        val weight = if (horizontal && !compact) 1f else 0f
        parent.addView(this, LinearLayout.LayoutParams(width, ViewGroup.LayoutParams.WRAP_CONTENT, weight).apply {
            topMargin = dp(10)
            if (parent.orientation == LinearLayout.HORIZONTAL) marginEnd = dp(8)
        })
    }

    private fun iconButton(parent: LinearLayout, iconResource: Int, descriptionResource: Int, click: () -> Unit): ImageButton = ImageButton(this).apply {
        setImageResource(iconResource)
        imageTintList = ColorStateList.valueOf(primary)
        background = rounded(subtle, dp(14).toFloat())
        contentDescription = getString(descriptionResource)
        scaleType = ImageView.ScaleType.CENTER_INSIDE
        setPadding(dp(11), dp(11), dp(11), dp(11))
        setOnClickListener { click() }
        parent.addView(this, LinearLayout.LayoutParams(dp(48), dp(48)).apply { marginEnd = dp(8) })
    }

    private fun menuItem(parent: LinearLayout, iconResource: Int, titleResource: Int, summary: String, click: () -> Unit) {
        val item = card(parent, surface, compact = true).apply {
            isClickable = true
            isFocusable = true
            minimumHeight = dp(72)
            contentDescription = "${getString(titleResource)}, $summary"
            setOnClickListener { click() }
        }
        val line = row(item)
        ImageView(this).apply {
            setImageResource(iconResource)
            imageTintList = ColorStateList.valueOf(primary)
            background = rounded(subtle, dp(12).toFloat())
            setPadding(dp(10), dp(10), dp(10), dp(10))
            line.addView(this, LinearLayout.LayoutParams(dp(44), dp(44)).apply { marginEnd = dp(12) })
        }
        val copy = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        line.addView(copy, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        label(copy, getString(titleResource), 16f, true)
        label(copy, summary, 13f, false, muted)
        TextView(this).apply {
            text = "›"
            textSize = 28f
            setTextColor(muted)
            gravity = Gravity.CENTER
            line.addView(this, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
    }

    private fun rounded(fill: Int, radius: Float, stroke: Int? = null): GradientDrawable = GradientDrawable().apply {
        shape = GradientDrawable.RECTANGLE
        setColor(fill)
        cornerRadius = radius
        if (stroke != null) setStroke(dp(1), stroke)
    }
    private fun outlined(color: Int) = rounded(surface, dp(12).toFloat(), color)
    private fun matchWrap(top: Int = 0, bottom: Int = 0) = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(top); bottomMargin = dp(bottom) }
    private fun dp(value: Int) = (value * resources.displayMetrics.density + 0.5f).toInt()
    private fun modeName(mode: AccessMode) = getString(when (mode) { AccessMode.READ_ONLY -> R.string.mode_read_only; AccessMode.SAFE -> R.string.mode_safe; AccessMode.READ_WRITE -> R.string.mode_read_write })

    private fun requestStart(selected: Int) {
        if (BatteryOptimizationPolicy.isExempt(this)) { beginSharing(selected); return }
        pendingStartFolder = selected
        status.setText(R.string.battery_optimization_required)
        val direct = BatteryOptimizationPolicy.requestIntent(packageName)
        try { startActivity(direct) }
        catch (_: android.content.ActivityNotFoundException) {
            try { startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)) }
            catch (_: android.content.ActivityNotFoundException) { pendingStartFolder = null; status.setText(R.string.battery_optimization_settings_failed) }
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
