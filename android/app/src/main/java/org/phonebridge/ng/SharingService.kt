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
import com.phonebridge.server.TlsHelper
import org.phonebridge.credentials.AccessMode
import org.phonebridge.credentials.PairedClient
import org.phonebridge.credentials.PairingStore
import org.phonebridge.credentials.StoreException
import java.io.File
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

internal object SharedFolders {
    val names = listOf("Music", "DCIM", "Pictures", "Download", "Movies", "Documents", "Android/media")
    val count get() = names.size + 1
    fun displayNames(context: Context) = names + context.getString(R.string.phone_storage)
    @Suppress("DEPRECATION")
    fun selected(index: Int): File {
        require(index in 0 until count)
        val root = Environment.getExternalStorageDirectory()
        val directory = if (index == names.size) root else File(root, names[index])
        require(directory.isDirectory)
        return directory
    }
    fun allowed(context: Context) = if (Build.VERSION.SDK_INT >= 30) Environment.isExternalStorageManager()
        else context.checkSelfPermission(Manifest.permission.READ_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED &&
            context.checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED
}

class SharingService : Service() {
    companion object {
        const val START = "org.phonebridge.ng.START"
        const val PAIR = "org.phonebridge.ng.PAIR"
        const val STOP = "org.phonebridge.ng.STOP"
        private const val CHANNEL = "sharing"
    }
    private val worker = Executors.newSingleThreadExecutor()
    @Volatile internal var engine: SharingEngine? = null
        private set
    @Volatile internal var status = R.string.stopped
        private set
    @Volatile internal var sharingEnabled = false
        private set
    @Volatile internal var selectedFolder = 0
        private set
    @Volatile internal var storedClients: List<PairedClient> = emptyList()
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
        fun closePairing() {
            foregroundGeneration.incrementAndGet()
            submit {
                engine?.pairing?.cancelWindow()
                if (!sharingEnabled) { stopEngine(); stopSelf() }
            }
        }
        fun decide(attempt: String, approve: Boolean) = submit { engine?.pairing?.decide(attempt, approve) ?: throw ApiFailure(409, "conflict") }
        fun revoke(client: String) = submit { engine?.revoke(client) ?: throw ApiFailure(409, "conflict") }
        fun remove(client: String) = submit {
            val current = engine
            if (current != null) { current.remove(client); storedClients = current.clients.clients }
            else { val store = pairingStore(); storedClients = store.remove(client, store.snapshot().revision).clients }
        }
        fun updateMode(client: String, mode: AccessMode) = submit {
            val current = engine
            if (current != null) { current.updateMode(client, mode); storedClients = current.clients.clients }
            else { val store = pairingStore(); storedClients = store.updateMode(client, mode, store.snapshot().revision).clients }
        }
    }
    private val binder = LocalBinder()
    override fun onBind(intent: Intent?): IBinder = binder
    override fun onCreate() {
        super.onCreate()
        getSystemService(NotificationManager::class.java).createNotificationChannel(NotificationChannel(CHANNEL, getString(R.string.sharing_channel), NotificationManager.IMPORTANCE_LOW))
        worker.execute {
            val existing = File(noBackupFilesDir, "pairings-v1").isDirectory
            storedClients = if (!existing) emptyList() else try { pairingStore().snapshot().clients } catch (_: Exception) {
                status = R.string.storage_error
                emptyList()
            }
        }
    }
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == STOP) {
            getSharedPreferences("settings", MODE_PRIVATE).edit().putBoolean("sharing", false).apply()
            worker.execute { stopEngine(); stopSelf() }; return START_NOT_STICKY
        }
        val prefs = getSharedPreferences("settings", MODE_PRIVATE)
        val action = intent?.action
        val resumeSharing = action == null && prefs.getBoolean("sharing", false)
        if (action !in setOf(START, PAIR) && !resumeSharing) { stopSelf(); return START_NOT_STICKY }
        val pairingOnly = action == PAIR
        startForeground(1, notification(if (pairingOnly && !sharingEnabled) R.string.pairing_notification else R.string.sharing_notification))
        val selected = if (action in setOf(START, PAIR)) intent?.getIntExtra("folder", 0) ?: 0 else prefs.getInt("folder", 0)
        worker.execute {
            try {
                if (action == START && engine != null && !sharingEnabled && selected != selectedFolder) stopEngine()
                if (engine == null) {
                    status = R.string.starting
                    check(SharedFolders.allowed(this))
                    val root = SharedFolders.selected(selected)
                    acquireRuntimeLocks()
                    val created = SharingEngine(applicationContext, root, 8273, { sharingEnabled }) {
                        worker.execute { status = R.string.storage_error; stopEngine(); stopSelf() }
                    }
                    engine = created; selectedFolder = selected; storedClients = created.clients.clients
                }
                if (pairingOnly) {
                    if (!sharingEnabled) {
                        check(prefs.edit().putBoolean("sharing", false).putInt("folder", selectedFolder).commit())
                        status = R.string.stopped
                    }
                    val generation = foregroundGeneration.get()
                    engine?.pairing?.openWindow { foreground.get() && foregroundGeneration.get() == generation }
                } else {
                    sharingEnabled = true
                    check(prefs.edit().putBoolean("sharing", true).putInt("folder", selectedFolder).commit())
                    status = R.string.sharing
                    startForeground(1, notification(R.string.sharing_notification))
                }
            } catch (_: Exception) { status = R.string.start_failed; stopEngine(); stopSelf() }
        }
        return START_STICKY
    }
    private fun submit(action: () -> Unit) {
        worker.execute {
            try { action(); if (engine != null) status = if (sharingEnabled) R.string.sharing else R.string.stopped }
            catch (_: StoreException) { status = R.string.storage_error; stopEngine(); stopSelf() }
            catch (e: ApiFailure) { status = if (e.status == 503) R.string.storage_error else R.string.action_rejected }
            catch (_: Exception) { status = R.string.action_rejected }
        }
    }
    private fun pairingStore(): PairingStore {
        check(File(noBackupFilesDir, "pairings-v1").isDirectory)
        val identity = TlsHelper.openIdentity(applicationContext)
        return PairingStore.openExisting(applicationContext, identity.fingerprint)
    }
    private fun notification(text: Int): Notification {
        val launch = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 1, Intent(this, SharingService::class.java).setAction(STOP), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        return Notification.Builder(this, CHANNEL).setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentTitle(getString(R.string.app_name)).setContentText(getString(text)).setContentIntent(launch)
            .setOngoing(true).addAction(Notification.Action.Builder(null, getString(R.string.stop), stop).build()).build()
    }
    private fun acquireRuntimeLocks() {
        check(runtimeLocks == null)
        runtimeLocks = SharingRuntimeLocks(applicationContext).also { it.acquire() }
    }
    private fun stopEngine() {
        val old = engine; engine = null
        sharingEnabled = false
        if (old != null) storedClients = old.clients.clients
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
