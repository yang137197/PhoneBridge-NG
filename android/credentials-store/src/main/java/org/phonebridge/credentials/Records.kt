package org.phonebridge.credentials

import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.security.MessageDigest
import java.util.Collections

enum class StoreError { INVALID_INPUT, NEEDS_REPAIR, STORAGE_FAILURE, BUSY, LOCKED, ALREADY_EXISTS, REVISION_CONFLICT, CAPACITY, UNAUTHORIZED }
class StoreException(val error: StoreError) : Exception("pairing_store_${error.name.lowercase()}")
enum class AccessMode(internal val wire: Int) { READ_ONLY(1), SAFE(2), READ_WRITE(3) }
enum class ClientState(internal val wire: Int) { ACTIVE(1), REVOKED(2) }
data class PairedClient(val clientId: String, val clientName: String, val mode: AccessMode, val state: ClientState)
class StoreSnapshot internal constructor(val revision: Long, clients: List<PairedClient>) {
    val clients: List<PairedClient> = Collections.unmodifiableList(ArrayList(clients))
    override fun toString(): String = "StoreSnapshot(redacted)"
}
internal class Entry(val client: PairedClient, val verifier: ByteArray)
internal class Database(var revision: Long, val entries: MutableList<Entry>) : AutoCloseable {
    fun snapshot() = StoreSnapshot(revision, entries.map { it.client })
    override fun close() { entries.forEach { it.verifier.fill(0) } }
}
internal object Rules {
    const val MAX_FILE = 65536
    fun hex(value: String, length: Int): Boolean = value.length == length && value.all { it in '0'..'9' || it in 'a'..'f' }
    fun bytes(value: String, length: Int): ByteArray {
        if (!hex(value, length * 2)) throw StoreException(StoreError.INVALID_INPUT)
        return ByteArray(length) { value.substring(it * 2, it * 2 + 2).toInt(16).toByte() }
    }
    fun hex(value: ByteArray): String = value.joinToString("") { "%02x".format(it.toInt() and 255) }
    fun name(value: String): ByteArray {
        try {
            require(value.isNotEmpty() && value.length <= 256 && value.codePointCount(0, value.length) <= 128)
            var i = 0
            while (i < value.length) {
                val cp = value.codePointAt(i); val category = Character.getType(cp)
                require(category != Character.CONTROL.toInt() && category != Character.FORMAT.toInt())
                i += Character.charCount(cp)
            }
            val encoded = Charsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT).encode(java.nio.CharBuffer.wrap(value))
            require(encoded.remaining() <= 256)
            return ByteArray(encoded.remaining()).also { encoded.get(it) }
        } catch (_: Exception) { throw StoreException(StoreError.INVALID_INPUT) }
    }
    fun verification(ca: ByteArray, clientId: String, token: ByteArray): ByteArray {
        if (ca.size != 32 || !hex(clientId, 32) || token.size != 32) throw StoreException(StoreError.INVALID_INPUT)
        val copy = token.copyOf()
        try {
            val digest = MessageDigest.getInstance("SHA-256")
            digest.update("PhoneBridge NG|credential|v1".toByteArray(Charsets.US_ASCII)); digest.update(0.toByte())
            digest.update(ca); digest.update(bytes(clientId, 16)); return digest.digest(copy)
        } finally { copy.fill(0) }
    }
}
internal object RecordCodec {
    fun encode(db: Database, ca: ByteArray): ByteArray {
        require(db.revision > 0 && ca.size == 32 && db.entries.size <= 128)
        val entries = db.entries.sortedBy { it.client.clientId }
        val names = entries.map { Rules.name(it.client.clientName) }
        val buffer = ByteBuffer.allocate(47 + names.sumOf { 52 + it.size })
        buffer.put("PBS1".toByteArray(Charsets.US_ASCII)).put(1).put(ca).putLong(db.revision).putShort(entries.size.toShort())
        entries.forEachIndexed { index, entry ->
            buffer.put(Rules.bytes(entry.client.clientId, 16)).put(entry.verifier)
            buffer.put(entry.client.mode.wire.toByte()).put(entry.client.state.wire.toByte())
            buffer.putShort(names[index].size.toShort()).put(names[index])
        }
        return buffer.array()
    }
    fun decode(bytes: ByteArray, ca: ByteArray): Database {
        val entries = mutableListOf<Entry>()
        try {
            require(bytes.size in 47..Rules.MAX_FILE)
            val b = ByteBuffer.wrap(bytes)
            fun take(n: Int): ByteArray { require(n >= 0 && n <= b.remaining()); return ByteArray(n).also { b.get(it) } }
            require(take(4).contentEquals("PBS1".toByteArray(Charsets.US_ASCII)) && b.get().toInt() == 1)
            require(MessageDigest.isEqual(take(32), ca)); val revision = b.long; require(revision > 0)
            val count = b.short.toInt() and 65535; require(count <= 128)
            var last = ""
            repeat(count) {
                val id = Rules.hex(take(16)); require(id > last); last = id
                val verifier = take(32)
                try {
                    val mode = AccessMode.entries.single { it.wire == b.get(b.position()).toInt() }; b.get()
                    val state = ClientState.entries.single { it.wire == b.get(b.position()).toInt() }; b.get()
                    val length = b.short.toInt() and 65535; require(length in 1..256)
                    val nameBytes = take(length)
                    val name = Charsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(nameBytes)).toString()
                    require(Rules.name(name).contentEquals(nameBytes))
                    entries.add(Entry(PairedClient(id, name, mode, state), verifier))
                } catch (e: Exception) { verifier.fill(0); throw e }
            }
            require(!b.hasRemaining() && entries.count { it.client.state == ClientState.ACTIVE } <= 16)
            return Database(revision, entries)
        } catch (_: Exception) { entries.forEach { it.verifier.fill(0) }; throw StoreException(StoreError.NEEDS_REPAIR) }
    }
}
