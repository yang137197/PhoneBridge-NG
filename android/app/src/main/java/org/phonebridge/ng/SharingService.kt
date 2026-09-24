package org.phonebridge.ng

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Binder
import android.os.Build
import android.os.Environment
import android.os.IBinder
import org.phonebridge.credentials.AccessMode
import org.phonebridge.credentials.StoreException
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

internal object SharedFolders {
    val names = listOf("Music", "DCIM", "Pictures", "Download", "Movies", "Documents", "Android/media")
    @Suppress("DEPRECATION")
    fun selected(index: Int): File {
        require(index in names.indices)
        val directory = File(Environment.getExternalStorageDirectory(), names[index])
        require(directory.isDirectory)
        return directory
    }
    fun allowed(context: Context) = if (Build.VERSION.SDK_INT >= 30) Environment.isExternalStorageManager()
        else context.checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED &&
            context.checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED
}

class SharingService : Service() {
    companion object { const val START = "org.phonebridge.ng.START"; const val STOP = "org.phonebridge.ng.STOP"; private const val CHANNEL = "sharing" }
    private val worker = Executors.newSingleThreadExecutor()
    @Volatile internal var engine: SharingEngine? = null
        private set
    @Volatile internal var status = R.string.stopped
        private set
    @Volatile internal var selectedFolder = 0
        private set
    private var runtimeLocks: SharingRuntimeLocks? = null
    internal val multicastLockHeld: Boolean get() = runtimeLocks?.multicastHeld == true
    internal val wakeLockHeld: Boolean get() = runtimeLocks?.wakeHeld == true
    private val foreground = AtomicBoolean(false)
    private val foregroundGeneration = AtomicLong(0)
    inner class LocalBinder : Binder() {
        internal val service get() = this@SharingService
        fun enterForeground() { foreground.set(true) }
        fun leaveForeground() { foreground.set(false); foregroundGeneration.incrementAndGet(); engine?.pairing?.cancelWindow() }
        fun openPairing() {
            val generation = foregroundGeneration.get()
            submit { engine?.pairing?.openWindow { foreground.get() && foregroundGeneration.get() == generation } ?: throw ApiFailure(409, "conflict") }
        }
        fun closePairing() { foregroundGeneration.incrementAndGet(); engine?.pairing?.cancelWindow() }
        fun decide(attempt: String, approve: Boolean) = submit { engine?.pairing?.decide(attempt, approve) ?: throw ApiFailure(409, "conflict") }
        fun revoke(client: String) = submit { engine?.revoke(client) ?: throw ApiFailure(409, "conflict") }
        fun remove(client: String) = submit { engine?.remove(client) ?: throw ApiFailure(409, "conflict") }
        fun updateMode(client: String, mode: AccessMode) = submit { engine?.updateMode(client, mode) ?: throw ApiFailure(409, "conflict") }
    }
    private val binder = LocalBinder()
    override fun onBind(intent: Intent?): IBinder = binder
    override fun onCreate() {
        super.onCreate()
        getSystemService(NotificationManager::class.java).createNotificationChannel(NotificationChannel(CHANNEL, getString(R.string.sharing_channel), NotificationManager.IMPORTANCE_LOW))
    }
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == STOP) {
            getSharedPreferences("settings", MODE_PRIVATE).edit().putBoolean("sharing", false).apply()
            worker.execute { stopEngine(); stopSelf() }; return START_NOT_STICKY
        }
        val prefs = getSharedPreferences("settings", MODE_PRIVATE)
        if (intent?.action != START && !prefs.getBoolean("sharing", false)) { stopSelf(); return START_NOT_STICKY }
        startForeground(1, notification())
        val selected = if (intent?.action == START) intent.getIntExtra("folder", 0) else prefs.getInt("folder", 0)
        worker.execute {
            if (engine != null) return@execute
            status = R.string.starting
            try {
                check(SharedFolders.allowed(this))
                val root = SharedFolders.selected(selected)
                acquireRuntimeLocks()
                val created = SharingEngine(applicationContext, root, 8273) { worker.execute { status = R.string.storage_error; stopEngine(); stopSelf() } }
                engine = created; selectedFolder = selected
                check(prefs.edit().putBoolean("sharing", true).putInt("folder", selected).commit())
                status = R.string.sharing
            } catch (_: Exception) { status = R.string.start_failed; stopEngine(); stopSelf() }
        }
        return START_STICKY
    }
    private fun submit(action: () -> Unit) {
        worker.execute {
            try { action(); if (engine != null) status = R.string.sharing }
            catch (_: StoreException) { status = R.string.storage_error; stopEngine(); stopSelf() }
            catch (e: ApiFailure) { status = if (e.status == 503) R.string.storage_error else R.string.action_rejected }
            catch (_: Exception) { status = R.string.action_rejected }
        }
    }
    private fun notification(): Notification {
        val launch = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 1, Intent(this, SharingService::class.java).setAction(STOP), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        return Notification.Builder(this, CHANNEL).setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentTitle(getString(R.string.app_name)).setContentText(getString(R.string.sharing_notification)).setContentIntent(launch)
            .setOngoing(true).addAction(Notification.Action.Builder(null, getString(R.string.stop), stop).build()).build()
    }
    private fun acquireRuntimeLocks() {
        check(runtimeLocks == null)
        runtimeLocks = SharingRuntimeLocks(applicationContext).also { it.acquire() }
    }
    private fun stopEngine() {
        val old = engine; engine = null
        try { old?.close() } catch (_: Exception) { }
        runtimeLocks?.close(); runtimeLocks = null
        stopForeground(STOP_FOREGROUND_REMOVE)
        if (status == R.string.sharing) status = R.string.stopped
    }
    override fun onTimeout(startId: Int, fgsType: Int) { worker.execute { stopEngine(); stopSelf() } }
    override fun onTaskRemoved(rootIntent: Intent?) {
        worker.execute {
            if (!getSharedPreferences("settings", MODE_PRIVATE).edit().putBoolean("sharing", false).commit()) {
                status = R.string.storage_error
            }
            stopEngine()
            stopSelf()
        }
        super.onTaskRemoved(rootIntent)
    }
    override fun onDestroy() {
        engine?.pairing?.cancelWindow()
        worker.execute { stopEngine() }; worker.shutdown()
        super.onDestroy()
    }
}
