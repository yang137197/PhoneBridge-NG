package org.phonebridge.ng

import android.app.Activity
import android.content.ComponentName
import android.content.Intent
import android.content.ServiceConnection
import android.os.Bundle
import android.os.IBinder
import android.os.SystemClock
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import androidx.test.runner.AndroidJUnitRunner
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.InetAddress
import java.net.ServerSocket
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

/** Test APK only. Loopback/ADB control data is transient and is never sent to instrumentation logs. */
class BridgeTestRunner : AndroidJUnitRunner() {
    private var bridge = false
    private var resume = false
    override fun onCreate(arguments: Bundle) { bridge = arguments.getString("bridge") == "true"; resume = arguments.getString("resume") == "true"; super.onCreate(arguments) }
    override fun onStart() {
        if (!bridge) { super.onStart(); return }
        val binder = AtomicReference<SharingService.LocalBinder?>()
        val bound = CountDownLatch(1)
        val connection = object : ServiceConnection {
            override fun onServiceConnected(name: ComponentName, service: IBinder) { binder.set(service as SharingService.LocalBinder); bound.countDown() }
            override fun onServiceDisconnected(name: ComponentName) { binder.set(null) }
        }
        var activity: Activity? = null; var registered = false; var success = false
        val recordFile = File(targetContext.noBackupFilesDir, "pairings-v1/records.bin")
        var originalRecord: ByteArray? = null
        fun launch() {
            if (activity?.let { !it.isFinishing && !it.isDestroyed } == true) return
            activity = startActivitySync(Intent(targetContext, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
        fun click(resource: Int) {
            val until = SystemClock.elapsedRealtime() + 10_000
            var clicked = false
            while (!clicked && SystemClock.elapsedRealtime() < until) {
                runOnMainSync {
                    fun search(view: View): Button? {
                        val expected = activity?.getString(resource) ?: targetContext.getString(resource)
                        if (view is Button && view.isEnabled && view.text.toString() == expected) return view
                        if (view is ViewGroup) for (i in 0 until view.childCount) search(view.getChildAt(i))?.let { return it }
                        return null
                    }
                    val button = activity?.window?.decorView?.let { search(it) }
                    clicked = button?.performClick() == true
                }
                if (!clicked) Thread.sleep(50)
            }
            check(clicked)
        }
        try {
            check(SharedFolders.allowed(targetContext))
            val folder = SharedFolders.selected(0)
            val origin = File(folder, "phonebridge-p1-007-origin.txt")
            if (origin.exists()) check(origin.readText() == "phonebridge-p1-007-synthetic")
            else origin.writeText("phonebridge-p1-007-synthetic")
            registered = targetContext.bindService(Intent(targetContext, SharingService::class.java), connection, android.content.Context.BIND_AUTO_CREATE)
            check(registered && bound.await(10, TimeUnit.SECONDS))
            launch(); click(R.string.start)
            val until = SystemClock.elapsedRealtime() + 15_000
            while (binder.get()?.service?.engine == null && SystemClock.elapsedRealtime() < until) Thread.sleep(50)
            check(binder.get()?.service?.engine != null)
            if (!resume) click(R.string.pair)
            ServerSocket(18190, 1, InetAddress.getByName("127.0.0.1")).use { server ->
                server.soTimeout = 180_000
                sendStatus(2, Bundle().apply { putString("bridge", "ready") })
                while (true) {
                    var finish = false
                    server.accept().use { socket ->
                        socket.soTimeout = 10_000
                        val bytes = ByteArrayOutputStream()
                        while (true) { val b = socket.getInputStream().read(); check(b >= 0); if (b == 10) break; bytes.write(b); check(bytes.size() <= 4096) }
                        val action = JSONObject(bytes.toString("UTF-8")).getString("action")
                        val engine = binder.get()?.service?.engine
                        val result = when (action) {
                            "info" -> {
                                val view = engine?.pairing?.view()
                                JSONObject().put("ready", engine != null).put("window", view?.windowId ?: JSONObject.NULL)
                                    .put("code", view?.code ?: JSONObject.NULL).put("pair_port", view?.port ?: 0)
                                    .put("https_port", engine?.httpsPort ?: 0).put("pending", view?.pendingName != null)
                            }
                            "approve" -> { click(R.string.approve); JSONObject().put("clicked", true) }
                            "pause" -> { runOnMainSync { activity?.finish() }; waitForIdleSync(); JSONObject().put("paused", true) }
                            "fault" -> {
                                check(originalRecord == null && engine != null)
                                originalRecord = recordFile.readBytes()
                                val damaged = originalRecord!!.copyOf()
                                damaged[damaged.lastIndex] = (damaged.last().toInt() xor 1).toByte()
                                recordFile.writeBytes(damaged)
                                JSONObject().put("injected", true)
                            }
                            "stop" -> {
                                if (engine != null) { launch(); click(R.string.stop) }
                                val deadline = SystemClock.elapsedRealtime() + 10_000
                                while (binder.get()?.service?.engine != null && SystemClock.elapsedRealtime() < deadline) Thread.sleep(50)
                                check(binder.get()?.service?.engine == null)
                                finish = true; JSONObject().put("stopped", true)
                            }
                            else -> error("unsupported test command")
                        }
                        socket.getOutputStream().write((result.toString()+"\n").toByteArray(Charsets.UTF_8)); socket.getOutputStream().flush()
                    }
                    if (finish) break
                }
            }
            success = true
        } catch (_: Exception) { /* Do not print a control packet, code, request or exception. */ }
        finally {
            targetContext.stopService(Intent(targetContext, SharingService::class.java))
            if (registered) targetContext.unbindService(connection)
            runOnMainSync { activity?.finish() }
            // Restore only this test application's synthetic ciphertext for repeatability; production never repairs it.
            originalRecord?.let { recordFile.writeBytes(it) }
            finish(if (success) Activity.RESULT_OK else Activity.RESULT_CANCELED, Bundle().apply { putString("bridge_result", if (success) "passed" else "failed") })
        }
    }
}
