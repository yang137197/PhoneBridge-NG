package com.phonebridge.server

// PhoneBridge NG, 2026-09-19. SPDX-License-Identifier: GPL-3.0-or-later
/** One byte range over the opened representation; unsupported units/multipart fall back to 200. */
internal sealed class HttpByteRange {
    data object Full : HttpByteRange()
    data object Unsatisfiable : HttpByteRange()
    data class Partial(val first: Long, val last: Long) : HttpByteRange() {
        val length: Long get() = last - first + 1
    }

    companion object {
        fun parse(header: String?, size: Long): HttpByteRange {
            require(size >= 0)
            if (header == null) return Full
            val value = header.trim()
            val separator = value.indexOf('=')
            if (separator < 0 || !value.substring(0, separator).equals("bytes", ignoreCase = true)) return Full
            val specification = value.substring(separator + 1)
            if (',' in specification) return Full // Multipart is not implemented; RFC 9110 permits ignoring Range.
            val match = Regex("([0-9]*)-([0-9]*)").matchEntire(specification) ?: return Unsatisfiable
            val (firstText, lastText) = match.destructured
            if (size == 0L || (firstText.isEmpty() && lastText.isEmpty())) return Unsatisfiable
            if (firstText.isEmpty()) {
                val suffix = lastText.toLongOrNull() ?: Long.MAX_VALUE
                if (suffix == 0L) return Unsatisfiable
                return Partial((size - suffix).coerceAtLeast(0), size - 1)
            }
            val first = firstText.toLongOrNull() ?: return Unsatisfiable
            if (first >= size) return Unsatisfiable
            val last = if (lastText.isEmpty()) size - 1 else (lastText.toLongOrNull() ?: Long.MAX_VALUE).coerceAtMost(size - 1)
            if (last < first) return Unsatisfiable
            return Partial(first, last)
        }
    }
}
