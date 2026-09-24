package com.phonebridge.server

import java.io.IOException
import java.net.URI

// PhoneBridge NG, 2026-09-19. SPDX-License-Identifier: GPL-3.0-or-later
internal class StorageFailure(val status: Int, message: String) : IOException(message)

/** NanoHTTPD has already decoded request paths. Never decode them again. */
internal class SharedPath private constructor(val parts: List<String>) {
    val isRoot get() = parts.isEmpty()
    val name get() = parts.lastOrNull() ?: ""
    val path get() = "/" + parts.joinToString("/")
    override fun equals(other: Any?) = other is SharedPath && parts == other.parts
    override fun hashCode() = parts.hashCode()
    fun child(name: String): SharedPath = parse((if (isRoot) "" else path) + "/" + name)
    fun requireChild() {
        if (isRoot) throw StorageFailure(403, "The shared root cannot be modified")
    }

    companion object {
        const val STAGING_PREFIX = ".phonebridge-upload-"

        fun parse(decoded: String): SharedPath {
            if (!decoded.startsWith('/') || decoded.any { it < ' ' || it == '\u007f' || it == '\\' }) {
                throw StorageFailure(403, "Invalid shared path")
            }
            if (decoded == "/") return SharedPath(emptyList())
            val parts = decoded.removeSuffix("/").substring(1).split('/')
            if (parts.any { it.isEmpty() || it == "." || it == ".." || it.startsWith(STAGING_PREFIX) }) {
                throw StorageFailure(403, "Invalid shared path")
            }
            return SharedPath(parts)
        }

        /** Destination is a raw header, so URI.path decodes it once and preserves literal '+'. */
        fun destination(raw: String?, host: String?): SharedPath {
            if (raw == null) throw StorageFailure(400, "Destination is required")
            val uri = try { URI(raw) } catch (_: Exception) { throw StorageFailure(400, "Invalid Destination") }
            if (uri.rawQuery != null || uri.rawFragment != null || uri.rawUserInfo != null || uri.isOpaque) {
                throw StorageFailure(400, "Invalid Destination")
            }
            if (uri.isAbsolute) {
                if (uri.scheme !in listOf("https", "http") || host == null ||
                    !uri.rawAuthority.equals(host, ignoreCase = true)) {
                    throw StorageFailure(403, "Destination must use this server")
                }
            } else if (uri.rawAuthority != null) {
                throw StorageFailure(403, "Destination must be a local absolute path")
            }
            return parse(uri.path ?: throw StorageFailure(400, "Invalid Destination"))
        }
    }
}
