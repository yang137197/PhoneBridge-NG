package com.phonebridge.server

// Modified by PhoneBridge NG, 2026-09-19: descriptor-backed metadata and escaped, thread-safe output.
// Based on ysachin26/PhoneBridge a378fec40561a4d18be2334f4de92ee02a0e7d0c; GPL-3.0-or-later.

import java.net.URLEncoder
import java.text.SimpleDateFormat
import java.util.*

internal object XmlResponseBuilder {
    fun buildPropfindResponse(entries: List<SharedEntry>): String = buildString {
        append("<?xml version=\"1.0\" encoding=\"utf-8\"?><D:multistatus xmlns:D=\"DAV:\">")
        val format = SimpleDateFormat("EEE, dd MMM yyyy HH:mm:ss 'GMT'", Locale.US).apply {
            timeZone = TimeZone.getTimeZone("GMT")
        }
        for (item in entries) {
            append("<D:response><D:href>${href(item)}</D:href><D:propstat><D:prop>")
            append("<D:displayname>${escapeXml(item.path.name)}</D:displayname>")
            append("<D:getlastmodified>${format.format(Date(item.modified))}</D:getlastmodified>")
            if (item.directory) append("<D:resourcetype><D:collection/></D:resourcetype>") else {
                append("<D:getcontentlength>${item.size}</D:getcontentlength>")
                append("<D:getcontenttype>${guessMimeType(item.path.name)}</D:getcontenttype><D:resourcetype/>")
            }
            append("</D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>")
        }
        append("</D:multistatus>")
    }

    fun href(item: SharedEntry): String = "/" + item.path.parts.joinToString("/") {
        URLEncoder.encode(it, "UTF-8").replace("+", "%20")
    } + if (item.directory && !item.path.isRoot) "/" else ""

    fun escapeXml(text: String): String = text.replace("&", "&amp;").replace("<", "&lt;")
        .replace(">", "&gt;").replace("\"", "&quot;").replace("'", "&apos;")

    fun guessMimeType(name: String): String {
        val ext = name.substringAfterLast('.', "").lowercase(Locale.ROOT)
        return when (ext) {
            "txt" -> "text/plain"
            "html", "htm" -> "text/html"
            "css" -> "text/css"
            "js" -> "application/javascript"
            "json" -> "application/json"
            "xml" -> "application/xml"
            "jpg", "jpeg" -> "image/jpeg"
            "png" -> "image/png"
            "gif" -> "image/gif"
            "webp" -> "image/webp"
            "svg" -> "image/svg+xml"
            "mp4" -> "video/mp4"
            "mp3" -> "audio/mpeg"
            "wav" -> "audio/wav"
            "ogg" -> "audio/ogg"
            "pdf" -> "application/pdf"
            "zip" -> "application/zip"
            "apk" -> "application/vnd.android.package-archive"
            "doc" -> "application/msword"
            "docx" -> "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            "xls" -> "application/vnd.ms-excel"
            "xlsx" -> "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            "ppt" -> "application/vnd.ms-powerpoint"
            "pptx" -> "application/vnd.openxmlformats-officedocument.presentationml.presentation"
            else -> "application/octet-stream"
        }
    }
}
