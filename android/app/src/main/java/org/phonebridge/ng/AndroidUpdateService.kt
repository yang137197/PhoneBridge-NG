package org.phonebridge.ng

import android.content.Context
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.os.Build
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

class AndroidUpdateService(private val context: Context) {
    companion object {
        const val LATEST_RELEASE_URL = "https://api.github.com/repos/yang137197/PhoneBridge-NG/releases/latest"
    }

    fun check(currentVersion: String): UpdateCheck {
        try {
            val connection = open(LATEST_RELEASE_URL)
            val body = connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
            if (connection.responseCode !in 200..299) throw UpdateFailure("update_check_failed")
            val release = JSONObject(body)
            val tag = release.optString("tag_name")
            val expectedVersion = tag.removePrefix("v")
            val expectedName = "PhoneBridge-NG-$expectedVersion.apk"
            val assets = release.optJSONArray("assets") ?: throw UpdateFailure("update_response_invalid")
            var match: JSONObject? = null
            for (index in 0 until assets.length()) {
                val candidate = assets.optJSONObject(index) ?: continue
                if (candidate.optString("name") == expectedName) { match = candidate; break }
            }
            val asset = match ?: throw UpdateFailure("update_asset_missing")
            return UpdatePolicy.evaluate(currentVersion, tag, release.optBoolean("draft", true),
                release.optBoolean("prerelease", true), asset.optString("name"), asset.optString("state"),
                asset.optLong("size", -1), asset.optString("digest"), asset.optString("browser_download_url"))
        } catch (failure: UpdateFailure) {
            throw failure
        } catch (error: Exception) {
            throw UpdateFailure("update_check_failed", error)
        }
    }

    internal fun download(asset: UpdateAsset, onProgress: (UpdateDownloadProgress) -> Unit = {}): PendingApk {
        val updateRoot = File(context.filesDir, "updates")
        val versionRoot = File(updateRoot, asset.version)
        val finalFile = File(versionRoot, asset.fileName)
        val partial = File(versionRoot, ".${asset.fileName}.${System.nanoTime()}.partial")
        try {
            ensureSafeDirectory(updateRoot)
            ensureSafeDirectory(versionRoot)
            val pending = PendingApk(finalFile, asset.version, asset.size, asset.sha256)
            onProgress(UpdateDownloadProgress(0, asset.size))
            if (finalFile.isFile && AndroidUpdateInstaller.verifyHash(pending)) {
                onProgress(UpdateDownloadProgress(asset.size, asset.size))
                return pending
            }
            if (finalFile.exists() && !finalFile.delete()) throw UpdateFailure("update_download_failed")

            val connection = open(asset.downloadUrl)
            if (connection.responseCode !in 200..299) throw UpdateFailure("update_download_failed")
            val advertised = connection.contentLengthLong
            if (advertised >= 0 && advertised != asset.size) throw UpdateFailure("update_integrity_failed")
            val digest = MessageDigest.getInstance("SHA-256")
            var total = 0L
            connection.inputStream.use { input ->
                partial.outputStream().buffered().use { output ->
                    val buffer = ByteArray(128 * 1024)
                    while (true) {
                        val read = input.read(buffer)
                        if (read < 0) break
                        total += read
                        if (total > asset.size || total > UpdatePolicy.MAX_APK_BYTES) throw UpdateFailure("update_integrity_failed")
                        digest.update(buffer, 0, read)
                        output.write(buffer, 0, read)
                        onProgress(UpdateDownloadProgress(total, asset.size))
                    }
                }
            }
            if (total != asset.size || !MessageDigest.isEqual(digest.digest(), asset.sha256.hexBytes()))
                throw UpdateFailure("update_integrity_failed")
            if (!partial.renameTo(finalFile)) throw UpdateFailure("update_download_failed")
            if (!AndroidUpdateInstaller.verifyHash(pending)) throw UpdateFailure("update_integrity_failed")
            return pending
        } catch (failure: UpdateFailure) {
            throw failure
        } catch (error: Exception) {
            throw UpdateFailure("update_download_failed", error)
        } finally {
            if (partial.exists()) partial.delete()
        }
    }

    private fun open(value: String): HttpURLConnection = (URL(value).openConnection() as HttpURLConnection).apply {
        connectTimeout = 15_000
        readTimeout = 30_000
        instanceFollowRedirects = true
        requestMethod = "GET"
        setRequestProperty("Accept", "application/vnd.github+json")
        setRequestProperty("User-Agent", "PhoneBridge-NG/${context.installedVersionName().substringBefore('-')}")
        setRequestProperty("X-GitHub-Api-Version", "2026-03-10")
    }

    private fun ensureSafeDirectory(directory: File) {
        if (!directory.exists() && !directory.mkdirs()) throw UpdateFailure("update_download_failed")
        val filesRoot = context.filesDir.canonicalFile
        val canonical = directory.canonicalFile
        if (!canonical.path.startsWith(filesRoot.path + File.separator) || canonical.isFile)
            throw UpdateFailure("update_download_failed")
    }
}

internal data class UpdateDownloadProgress(val downloadedBytes: Long, val totalBytes: Long) {
    val percent: Int = if (totalBytes <= 0) 0 else
        ((downloadedBytes.coerceIn(0, totalBytes) * 100L) / totalBytes).toInt()
}

internal fun Context.installedVersionName(): String {
    val info: PackageInfo = if (Build.VERSION.SDK_INT >= 33) {
        packageManager.getPackageInfo(packageName, PackageManager.PackageInfoFlags.of(0))
    } else {
        @Suppress("DEPRECATION")
        packageManager.getPackageInfo(packageName, 0)
    }
    return info.versionName ?: throw UpdateFailure("update_current_version_invalid")
}

internal fun String.hexBytes(): ByteArray {
    if (length % 2 != 0) throw IllegalArgumentException("invalid hex")
    return ByteArray(length / 2) { index -> substring(index * 2, index * 2 + 2).toInt(16).toByte() }
}
