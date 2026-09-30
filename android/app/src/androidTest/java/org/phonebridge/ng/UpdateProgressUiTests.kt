package org.phonebridge.ng

import android.app.AlertDialog
import android.os.ParcelFileDescriptor
import android.widget.ProgressBar
import android.widget.TextView
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class UpdateProgressUiTests {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private lateinit var activity: MainActivity

    @Before
    fun launchActivity() {
        val monitor = instrumentation.addMonitor(MainActivity::class.java.name, null, false)
        val component = "${instrumentation.targetContext.packageName}/${MainActivity::class.java.name}"
        ParcelFileDescriptor.AutoCloseInputStream(
            instrumentation.uiAutomation.executeShellCommand("am start -W -n $component")
        ).bufferedReader().use { it.readText() }
        activity = monitor.waitForActivityWithTimeout(10_000) as? MainActivity
            ?: error("MainActivity did not start")
        instrumentation.removeMonitor(monitor)
    }

    @After
    fun closeActivity() {
        if (::activity.isInitialized) {
            instrumentation.runOnMainSync {
                invoke("dismissUpdateProgress")
                activity.finish()
            }
            instrumentation.waitForIdleSync()
        }
    }

    @Test
    fun downloadDialogShowsDeterminatePercentage() {
        instrumentation.runOnMainSync {
            invoke("showUpdateProgress", String::class.java, "0.2.6")
            assertProgress(0)

            invoke(
                "renderUpdateProgress",
                String::class.java,
                UpdateDownloadProgress::class.java,
                "0.2.6",
                UpdateDownloadProgress(50, 100)
            )
            assertProgress(50)

            invoke(
                "renderUpdateProgress",
                String::class.java,
                UpdateDownloadProgress::class.java,
                "0.2.6",
                UpdateDownloadProgress(100, 100)
            )
            assertProgress(100)
        }
    }

    private fun assertProgress(percent: Int) {
        val dialog = field<AlertDialog>("updateProgressDialog")
        val progress = field<ProgressBar>("updateProgressBar")
        val label = field<TextView>("updateProgressLabel")
        val expected = activity.getString(R.string.update_downloading_progress, "0.2.6", percent)

        assertTrue(dialog.isShowing)
        assertEquals(percent, progress.progress)
        assertEquals(expected, label.text.toString())
        assertEquals(expected, progress.contentDescription.toString())
    }

    private fun invoke(name: String, vararg signatureAndArguments: Any) {
        val split = signatureAndArguments.indexOfFirst { it !is Class<*> }
        val signature = if (split < 0) signatureAndArguments else signatureAndArguments.copyOfRange(0, split)
        val arguments = if (split < 0) emptyArray() else signatureAndArguments.copyOfRange(split, signatureAndArguments.size)
        MainActivity::class.java.getDeclaredMethod(name, *signature.map { it as Class<*> }.toTypedArray()).apply {
            isAccessible = true
            invoke(activity, *arguments)
        }
    }

    @Suppress("UNCHECKED_CAST")
    private fun <T> field(name: String): T = MainActivity::class.java.getDeclaredField(name).run {
        isAccessible = true
        get(activity) as T
    }
}
