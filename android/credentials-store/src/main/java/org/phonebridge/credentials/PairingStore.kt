package org.phonebridge.credentials

import android.content.Context
import android.os.UserManager
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap

/** Storage only. The host must perform user approval, strict TLS, request cancellation and stop sharing on failure. */
class PairingStore private constructor(inputContext: Context, caSha256: String, name: String,
    private val checkpoint: ((CommitStage) -> Unit)?) {
    private val context = inputContext.applicationContext ?: inputContext
    private val ca = Rules.bytes(caSha256, 32)
    private val root: File
    private val protection: KeystoreProtection
    private val gate: Gate
    private class Gate { var poisoned = false }
    init {
        if (!name.matches(Regex("[a-z0-9-]{1,64}"))) throw StoreException(StoreError.INVALID_INPUT)
        if (inputContext.isDeviceProtectedStorage || !inputContext.getSystemService(UserManager::class.java).isUserUnlocked)
            throw StoreException(StoreError.LOCKED)
        root = File(this.context.noBackupFilesDir, name)
        protection = KeystoreProtection("org.phonebridge.ng.$name.aes-v1", ca)
        gate = gates.getOrPut(root.absolutePath) { Gate() }
    }
    companion object {
        private val gates = ConcurrentHashMap<String, Gate>()
        /** Must be called only by the host's new identity creation transaction, never as a load-error fallback. */
        fun initializeForNewIdentity(context: Context, caSha256: String): PairingStore =
            PairingStore(context, caSha256, "pairings-v1", null).also { it.initialize() }
        fun openExisting(context: Context, caSha256: String): PairingStore =
            PairingStore(context, caSha256, "pairings-v1", null).also { it.snapshot() }
        internal fun testStore(context: Context, caSha256: String, name: String, checkpoint: ((CommitStage) -> Unit)? = null) = PairingStore(context, caSha256, name, checkpoint)
    }
    internal fun initialize() = transaction(true) { files ->
        if (protection.exists() || files.exists()) throw StoreException(StoreError.ALREADY_EXISTS)
        try {
            protection.create(); checkpoint?.invoke(CommitStage.KEY_CREATED)
            Database(1, mutableListOf()).use { commit(files, it) }
        } catch (_: Exception) { gate.poisoned = true; throw StoreException(StoreError.STORAGE_FAILURE) }
    }
    fun snapshot(): StoreSnapshot = transaction { files -> read(files).use { it.snapshot() } }
    fun keyProtection(): KeyProtectionInfo = transaction { files -> read(files).use { protection.protectionInfo() } }
    fun approve(clientId: String, clientName: String, token: ByteArray, expectedRevision: Long, mode: AccessMode = AccessMode.SAFE): StoreSnapshot {
        Rules.name(clientName); val verifier = Rules.verification(ca, clientId, token)
        try {
            return transaction { files -> read(files).use { db ->
                revision(db, expectedRevision)
                if (db.entries.any { it.client.clientId == clientId }) throw StoreException(StoreError.ALREADY_EXISTS)
                if (db.entries.size >= 128 || db.entries.count { it.client.state == ClientState.ACTIVE } >= 16) throw StoreException(StoreError.CAPACITY)
                db.entries.add(Entry(PairedClient(clientId, clientName, mode, ClientState.ACTIVE), verifier.copyOf()))
                advance(db); commit(files, db); db.snapshot()
            } }
        } finally { verifier.fill(0) }
    }
    fun revoke(clientId: String, expectedRevision: Long): StoreSnapshot {
        if (!Rules.hex(clientId, 32)) throw StoreException(StoreError.INVALID_INPUT)
        return transaction { files -> read(files).use { db ->
            revision(db, expectedRevision)
            val index = db.entries.indexOfFirst { it.client.clientId == clientId }
            if (index < 0) throw StoreException(StoreError.UNAUTHORIZED)
            val old = db.entries[index]
            if (old.client.state == ClientState.ACTIVE) {
                db.entries[index] = Entry(old.client.copy(state = ClientState.REVOKED), old.verifier)
                advance(db); commit(files, db)
            }
            db.snapshot()
        } }
    }
    fun remove(clientId: String, expectedRevision: Long): StoreSnapshot {
        if (!Rules.hex(clientId, 32)) throw StoreException(StoreError.INVALID_INPUT)
        return transaction { files -> read(files).use { db ->
            revision(db, expectedRevision)
            val index = db.entries.indexOfFirst { it.client.clientId == clientId }
            if (index < 0) throw StoreException(StoreError.UNAUTHORIZED)
            val removed = db.entries.removeAt(index)
            try {
                advance(db); commit(files, db); db.snapshot()
            } finally { removed.verifier.fill(0) }
        } }
    }
    fun updateMode(clientId: String, mode: AccessMode, expectedRevision: Long): StoreSnapshot = transaction { files -> read(files).use { db ->
        revision(db, expectedRevision)
        val index = db.entries.indexOfFirst { it.client.clientId == clientId && it.client.state == ClientState.ACTIVE }
        if (index < 0) throw StoreException(StoreError.UNAUTHORIZED)
        val old = db.entries[index]
        if (old.client.mode != mode) { db.entries[index] = Entry(old.client.copy(mode = mode), old.verifier); advance(db); commit(files, db) }
        db.snapshot()
    } }
    /** Snapshot for one request; never cache as a session-wide authorization. */
    fun authenticate(clientId: String, token: ByteArray): PairedClient = withAuthorizedCommit(clientId, token) { it }
    /** Keep the callback short: recheck authorization and commit under the same lock used by revoke. */
    fun <T> withAuthorizedCommit(clientId: String, token: ByteArray, commit: (PairedClient) -> T): T {
        if (!Rules.hex(clientId, 32) || token.size != 32) throw StoreException(StoreError.UNAUTHORIZED)
        val verifier = Rules.verification(ca, clientId, token)
        try {
            return transaction { files -> read(files).use { db ->
                val entry = db.entries.singleOrNull { it.client.clientId == clientId && it.client.state == ClientState.ACTIVE }
                val matches = MessageDigest.isEqual(entry?.verifier ?: ByteArray(32), verifier)
                if (entry == null || !matches) throw StoreException(StoreError.UNAUTHORIZED)
                commit(entry.client)
            } }
        } finally { verifier.fill(0) }
    }
    private fun revision(db: Database, expected: Long) { if (db.revision != expected) throw StoreException(StoreError.REVISION_CONFLICT) }
    private fun advance(db: Database) { if (db.revision == Long.MAX_VALUE) throw StoreException(StoreError.NEEDS_REPAIR); db.revision++ }
    private fun read(files: PrivateFiles): Database {
        if (!protection.exists() || !files.exists()) throw StoreException(StoreError.NEEDS_REPAIR)
        val plaintext = protection.decrypt(files.read())
        try { return RecordCodec.decode(plaintext, ca) } finally { plaintext.fill(0) }
    }
    private fun commit(files: PrivateFiles, db: Database) {
        try {
            val plaintext = RecordCodec.encode(db, ca)
            val hash: ByteArray; val encrypted: ByteArray
            try { hash = MessageDigest.getInstance("SHA-256").digest(plaintext); encrypted = protection.encrypt(plaintext) } finally { plaintext.fill(0) }
            checkpoint?.invoke(CommitStage.ENCRYPTED); files.write(encrypted, checkpoint)
            read(files).use { verified ->
                val check = RecordCodec.encode(verified, ca)
                try { if (!MessageDigest.isEqual(hash, MessageDigest.getInstance("SHA-256").digest(check))) throw StoreException(StoreError.NEEDS_REPAIR) }
                finally { check.fill(0) }
            }
        } catch (_: Exception) { gate.poisoned = true; throw StoreException(StoreError.STORAGE_FAILURE) }
    }
    private fun <T> transaction(create: Boolean = false, action: (PrivateFiles) -> T): T = synchronized(gate) {
        if (gate.poisoned) throw StoreException(StoreError.NEEDS_REPAIR)
        if (context.isDeviceProtectedStorage || !context.getSystemService(UserManager::class.java).isUserUnlocked) throw StoreException(StoreError.LOCKED)
        try { PrivateFiles.acquire(root, create).use(action) }
        catch (e: StoreException) {
            if (e.error in setOf(StoreError.NEEDS_REPAIR, StoreError.STORAGE_FAILURE)) gate.poisoned = true
            throw e
        } catch (_: Exception) { gate.poisoned = true; throw StoreException(StoreError.NEEDS_REPAIR) }
    }
}
