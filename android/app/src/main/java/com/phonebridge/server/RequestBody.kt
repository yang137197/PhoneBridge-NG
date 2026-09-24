package com.phonebridge.server

import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.net.SocketTimeoutException

// PhoneBridge NG, 2026-09-19. SPDX-License-Identifier: GPL-3.0-or-later
internal object RequestBody {
    /** Consume exactly the declared length; the next persistent request must remain unread. */
    fun discard(input: InputStream, length: Long, deadlineNanos: Long) {
        require(length >= 0)
        val buffer = ByteArray(8192)
        var remaining = length
        while (remaining > 0) {
            if (System.nanoTime() - deadlineNanos >= 0) throw SocketTimeoutException("Request body deadline exceeded")
            val count = input.read(buffer, 0, minOf(remaining, buffer.size.toLong()).toInt())
            if (count < 0) throw EOFException("Incomplete request body")
            if (count == 0) throw IOException("Request body made no progress")
            remaining -= count
        }
    }
}
