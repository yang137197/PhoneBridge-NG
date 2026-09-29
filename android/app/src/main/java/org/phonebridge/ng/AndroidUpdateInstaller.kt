package org.phonebridge.ng

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.os.Build
import android.widget.Toast
import java.io.File
import java.security.MessageDigest

data class PendingApk(val file: File, val version: String, val size: Long, val sha256: String)

object AndroidUpdateInstaller {
    const val STATUS_ACTION = "org.phonebridge.ng.UPDATE_INSTALL_STATUS"

    fun verify(context: Context, pending: PendingApk): Boolean {
        if (!verifyHash(pending)) return false
        val manager = context.packageManager
        val archive = archivePackageInfo(manager, pending.file.absolutePath) ?: return false
        val installed = try { installedPackageInfo(manager, context.packageName) } catch (_: PackageManager.NameNotFoundException) { null } ?: return false
        if (archive.packageName != context.packageName || archive.versionName != pending.version ||
            versionCode(archive) <= versionCode(installed)) return false
        val archiveSigners = signerDigests(archive)
        return archiveSigners.isNotEmpty() && archiveSigners == signerDigests(installed)
    }

    fun verifyHash(pending: PendingApk): Boolean = try {
        if (!pending.file.isFile || pending.file.length() != pending.size || pending.sha256.length != 64) false
        else {
            val digest = MessageDigest.getInstance("SHA-256")
            pending.file.inputStream().use { input ->
                val buffer = ByteArray(128 * 1024)
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    digest.update(buffer, 0, read)
                }
            }
            MessageDigest.isEqual(digest.digest(), pending.sha256.hexBytes())
        }
    } catch (_: Exception) { false }

    fun install(context: Context, pending: PendingApk) {
        if (!verify(context, pending)) throw UpdateFailure("update_apk_invalid")
        val installer = context.packageManager.packageInstaller
        val parameters = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(context.packageName)
            setInstallReason(PackageManager.INSTALL_REASON_USER)
            if (Build.VERSION.SDK_INT >= 31) setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_REQUIRED)
            if (Build.VERSION.SDK_INT >= 33) setPackageSource(PackageInstaller.PACKAGE_SOURCE_DOWNLOADED_FILE)
        }
        val sessionId = installer.createSession(parameters)
        try {
            installer.openSession(sessionId).use { session ->
                pending.file.inputStream().use { input ->
                    session.openWrite(pending.file.name, 0, pending.size).use { output ->
                        input.copyTo(output, 128 * 1024)
                        session.fsync(output)
                    }
                }
                val status = Intent(context, UpdateInstallReceiver::class.java).setAction(STATUS_ACTION)
                val callback = PendingIntent.getBroadcast(context, sessionId, status,
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE)
                session.commit(callback.intentSender)
            }
        } catch (error: Exception) {
            runCatching { installer.abandonSession(sessionId) }
            throw UpdateFailure("update_install_failed", error)
        }
    }

    @Suppress("DEPRECATION")
    private fun archivePackageInfo(manager: PackageManager, archivePath: String): PackageInfo? =
        if (Build.VERSION.SDK_INT >= 28)
            manager.getPackageArchiveInfo(archivePath, PackageManager.GET_SIGNING_CERTIFICATES)
        else manager.getPackageArchiveInfo(archivePath, PackageManager.GET_SIGNATURES)

    @Suppress("DEPRECATION")
    private fun installedPackageInfo(manager: PackageManager, packageName: String): PackageInfo =
        if (Build.VERSION.SDK_INT >= 33) manager.getPackageInfo(packageName,
            PackageManager.PackageInfoFlags.of(PackageManager.GET_SIGNING_CERTIFICATES.toLong()))
        else if (Build.VERSION.SDK_INT >= 28)
            manager.getPackageInfo(packageName, PackageManager.GET_SIGNING_CERTIFICATES)
        else manager.getPackageInfo(packageName, PackageManager.GET_SIGNATURES)

    @Suppress("DEPRECATION")
    private fun signerDigests(info: PackageInfo): Set<String> {
        val signatures = if (Build.VERSION.SDK_INT >= 28) info.signingInfo?.apkContentsSigners ?: emptyArray()
            else info.signatures ?: emptyArray()
        return signatures.map { signature ->
            MessageDigest.getInstance("SHA-256").digest(signature.toByteArray()).joinToString("") { "%02X".format(it) }
        }.toSet()
    }

    @Suppress("DEPRECATION")
    private fun versionCode(info: PackageInfo): Long = if (Build.VERSION.SDK_INT >= 28) info.longVersionCode else info.versionCode.toLong()
}

class UpdateInstallReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != AndroidUpdateInstaller.STATUS_ACTION) return
        when (intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                @Suppress("DEPRECATION")
                val confirmation = if (Build.VERSION.SDK_INT >= 33)
                    intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent::class.java)
                else intent.getParcelableExtra(Intent.EXTRA_INTENT) as? Intent
                confirmation?.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                if (confirmation != null) context.startActivity(confirmation)
                else Toast.makeText(context, R.string.update_install_failed, Toast.LENGTH_LONG).show()
            }
            PackageInstaller.STATUS_SUCCESS -> Unit
            else -> Toast.makeText(context, R.string.update_install_failed, Toast.LENGTH_LONG).show()
        }
    }
}
