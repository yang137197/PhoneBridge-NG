package org.phonebridge.ng

data class UpdateAsset(
    val version: String,
    val fileName: String,
    val downloadUrl: String,
    val size: Long,
    val sha256: String,
)

enum class UpdateAvailability { CURRENT, AVAILABLE }

data class UpdateCheck(val availability: UpdateAvailability, val currentVersion: String, val asset: UpdateAsset)

class UpdateFailure(val code: String, cause: Throwable? = null) : Exception(code, cause)

object UpdatePolicy {
    const val MAX_APK_BYTES = 256L * 1024 * 1024
    private val versionPattern = Regex("^(v?)(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")
    private val digestPattern = Regex("^[0-9A-F]{64}$")

    fun evaluate(currentVersion: String, tag: String, draft: Boolean, prerelease: Boolean,
                 name: String, state: String, size: Long, digest: String, downloadUrl: String): UpdateCheck {
        val current = parse(currentVersion.substringBefore('-'), allowPrefix = false)
            ?: throw UpdateFailure("update_current_version_invalid")
        val latest = parse(tag, allowPrefix = true) ?: throw UpdateFailure("update_response_invalid")
        if (draft || prerelease) throw UpdateFailure("update_response_invalid")
        val display = latest.second
        val expectedName = "PhoneBridge-NG-$display.apk"
        val expectedUrl = "https://github.com/yang137197/PhoneBridge-NG/releases/download/v$display/$expectedName"
        val normalizedDigest = digest.removePrefix("sha256:").uppercase()
        if (name != expectedName || state != "uploaded" || size !in 1..MAX_APK_BYTES ||
            !digest.startsWith("sha256:", ignoreCase = true) || !digestPattern.matches(normalizedDigest) ||
            downloadUrl != expectedUrl) throw UpdateFailure("update_response_invalid")
        val asset = UpdateAsset(display, expectedName, expectedUrl, size, normalizedDigest)
        return UpdateCheck(if (compare(latest.first, current.first) > 0) UpdateAvailability.AVAILABLE else UpdateAvailability.CURRENT,
            current.second, asset)
    }

    private fun parse(value: String, allowPrefix: Boolean): Pair<List<Long>, String>? {
        val match = versionPattern.matchEntire(value) ?: return null
        if (!allowPrefix && match.groupValues[1].isNotEmpty()) return null
        val numbers = match.groupValues.drop(2).map { it.toLongOrNull() ?: return null }
        return numbers to numbers.joinToString(".")
    }

    private fun compare(left: List<Long>, right: List<Long>): Int {
        for (index in 0..2) {
            val result = left[index].compareTo(right[index])
            if (result != 0) return result
        }
        return 0
    }
}
