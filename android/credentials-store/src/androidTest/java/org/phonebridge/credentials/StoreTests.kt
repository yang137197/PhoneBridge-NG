package org.phonebridge.credentials

import android.content.Context
import android.system.Os
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.security.KeyStore
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

@RunWith(AndroidJUnit4::class)
class StoreTests {
    private lateinit var context: Context
    private lateinit var name: String
    private val ca = "11".repeat(32)
    private val client = "22".repeat(16)
    private val token = ByteArray(32) { (it + 1).toByte() }
    private val root get() = File(context.noBackupFilesDir, name)
    private val record get() = File(root, "records.bin")
    private val alias get() = "org.phonebridge.ng.$name.aes-v1"
    @Before fun setup() { context = InstrumentationRegistry.getInstrumentation().targetContext; name = "test-${UUID.randomUUID()}" }
    private fun store(hook: ((CommitStage) -> Unit)? = null, hash: String = ca) = PairingStore.testStore(context, hash, name, hook)
    private fun fresh() = store().also { it.initialize() }
    private fun active(): Pair<PairingStore, StoreSnapshot> { val s = fresh(); return s to s.approve(client, "合成电脑", token, 1) }
    private fun error(expected: StoreError, block: () -> Unit) {
        try { block(); fail("expected fixed StoreError") } catch (e: StoreException) { assertEquals(expected, e.error); assertNull(e.cause) }
    }
    private fun keyStore() = KeyStore.getInstance("AndroidKeyStore").also { it.load(null) }
    private fun protection() = KeystoreProtection(alias, Rules.bytes(ca, 32))
    private fun mutate(mutation: (ByteArray) -> ByteArray) {
        val plain = protection().decrypt(record.readBytes())
        try { val changed = mutation(plain); try { record.writeBytes(protection().encrypt(changed)) } finally { changed.fill(0) } }
        finally { plain.fill(0) }
    }
    @Test fun verificationKnownAnswerAndBindings() {
        assertEquals("61aa4cef7ac3e7a2a96ef69f518103b1e9465b9f94ac32982d00d04b9ad0eba3", Rules.hex(Rules.verification(Rules.bytes(ca, 32), client, token)))
        assertFalse(Rules.verification(Rules.bytes(ca, 32), client, token).contentEquals(Rules.verification(ByteArray(32), client, token)))
        assertFalse(Rules.verification(Rules.bytes(ca, 32), client, token).contentEquals(Rules.verification(Rules.bytes(ca, 32), "33".repeat(16), token)))
    }
    @Test fun nativeRoundTripDefaultModeAndNoRawToken() {
        val (s, snap) = active(); assertEquals(2L, snap.revision); assertEquals(AccessMode.SAFE, s.authenticate(client, token).mode)
        assertEquals(snap.revision, store().snapshot().revision)
        val disk = record.readBytes(); assertEquals(-1, disk.indexOfSlice(token))
        val plain = protection().decrypt(disk)
        try { assertEquals(-1, plain.indexOfSlice(token)); assertTrue(plain.indexOfSlice(Rules.verification(Rules.bytes(ca, 32), client, token)) >= 0) }
        finally { plain.fill(0) }
        assertEquals(384, Os.stat(record.path).st_mode and 4095); assertEquals(448, Os.stat(root.path).st_mode and 4095)
        assertEquals(1L, Os.stat(record.path).st_nlink); assertTrue(record.path.startsWith(context.noBackupFilesDir.path + "/"))
        assertEquals("StoreSnapshot(redacted)", snap.toString())
    }
    @Test fun wrongUnknownAndMalformedCredentialsAreUniformlyRefused() {
        val (s, _) = active()
        error(StoreError.UNAUTHORIZED) { s.authenticate(client, ByteArray(32)) }
        error(StoreError.UNAUTHORIZED) { s.authenticate("33".repeat(16), token) }
        error(StoreError.UNAUTHORIZED) { s.authenticate("bad", token) }
        error(StoreError.UNAUTHORIZED) { s.authenticate(client, ByteArray(31)) }
        assertEquals(ClientState.ACTIVE, s.authenticate(client, token).state)
    }
    @Test fun revocationPersistsAndCannotReactivateSameClient() {
        val (s, snap) = active(); val revoked = s.revoke(client, snap.revision)
        error(StoreError.UNAUTHORIZED) { store().authenticate(client, token) }
        assertEquals(ClientState.REVOKED, store().snapshot().clients.single().state)
        assertEquals(revoked.revision, s.revoke(client, revoked.revision).revision)
        error(StoreError.ALREADY_EXISTS) { s.approve(client, "new", token, revoked.revision) }
        error(StoreError.UNAUTHORIZED) { s.updateMode(client, AccessMode.READ_WRITE, revoked.revision) }
    }
    @Test fun localRemovalDeletesRecordAndAllowsFreshPairing() {
        val (s, snap) = active(); val removed = s.remove(client, snap.revision)
        assertTrue(removed.clients.isEmpty()); assertTrue(store().snapshot().clients.isEmpty())
        error(StoreError.UNAUTHORIZED) { s.authenticate(client, token) }
        val fresh = s.approve(client, "new computer", token, removed.revision)
        assertEquals(ClientState.ACTIVE, fresh.clients.single().state)
    }
    @Test fun revisionsProtectModeAndRevocationFromStaleCallers() {
        val (s, snap) = active(); val changed = s.updateMode(client, AccessMode.READ_ONLY, snap.revision)
        assertEquals(AccessMode.READ_ONLY, store().authenticate(client, token).mode)
        error(StoreError.REVISION_CONFLICT) { s.revoke(client, snap.revision) }
        assertEquals(changed.revision, s.updateMode(client, AccessMode.READ_ONLY, changed.revision).revision)
        error(StoreError.ALREADY_EXISTS) { s.approve(client, "different", ByteArray(32), changed.revision) }
    }
    @Test fun deviceNotePersistsWithoutChangingAuthorizationOrMode() {
        val (s, snap) = active()
        val changed = s.updateDeviceNote(client, "  家里电脑  ", snap.revision)
        assertEquals("家里电脑", changed.clients.single().deviceNote)
        assertEquals(AccessMode.SAFE, store().authenticate(client, token).mode)
        assertEquals("家里电脑", store().snapshot().clients.single().deviceNote)
        assertEquals(changed.revision, s.updateDeviceNote(client, "家里电脑", changed.revision).revision)
        error(StoreError.REVISION_CONFLICT) { s.updateDeviceNote(client, "旧写入", snap.revision) }
        error(StoreError.INVALID_INPUT) { s.updateDeviceNote(client, "a".repeat(65), changed.revision) }
        error(StoreError.INVALID_INPUT) { s.updateDeviceNote(client, "bad\u0000note", changed.revision) }
        val cleared = s.updateDeviceNote(client, "", changed.revision)
        assertEquals("", cleared.clients.single().deviceNote)
    }
    @Test fun legacyVersionOneRecordLoadsWithEmptyDeviceNoteAndUpgradesOnWrite() {
        val (s, snap) = active()
        val plain = protection().decrypt(record.readBytes())
        try {
            RecordCodec.decode(plain, Rules.bytes(ca, 32)).use { db ->
                val legacy = RecordCodec.encode(db, Rules.bytes(ca, 32), 1)
                try { record.writeBytes(protection().encrypt(legacy)) } finally { legacy.fill(0) }
            }
        } finally { plain.fill(0) }
        assertEquals("", store().snapshot().clients.single().deviceNote)
        val upgraded = s.updateDeviceNote(client, "Office PC", snap.revision)
        assertEquals("Office PC", upgraded.clients.single().deviceNote)
        val upgradedPlain = protection().decrypt(record.readBytes())
        try { assertEquals(2, upgradedPlain[4].toInt()) } finally { upgradedPlain.fill(0) }
    }
    @Test fun twoClientsRemainIndependent() {
        val (s, first) = active(); val other = "33".repeat(16); val otherToken = ByteArray(32) { 9 }
        val second = s.approve(other, "second", otherToken, first.revision, AccessMode.READ_ONLY)
        error(StoreError.UNAUTHORIZED) { s.authenticate(other, token) }
        s.revoke(client, second.revision); assertEquals(AccessMode.READ_ONLY, store().authenticate(other, otherToken).mode)
    }
    @Test fun sixteenActiveCapacityIsEnforcedWithoutEviction() {
        val s = fresh(); var revision = 1L
        for (i in 1..16) revision = s.approve(i.toString(16).padStart(32, '0'), "pc", token, revision).revision
        val old = record.readBytes()
        error(StoreError.CAPACITY) { s.approve("ff".repeat(16), "too many", token, revision) }; assertArrayEquals(old, record.readBytes())
        revision = s.revoke("1".padStart(32, '0'), revision).revision
        assertEquals(16, s.approve("ff".repeat(16), "replacement", token, revision).clients.count { it.state == ClientState.ACTIVE })
    }
    @Test fun totalRecordCapacityDoesNotDiscardRevocations() {
        val s = fresh()
        Database(1, (1..128).map { Entry(PairedClient(it.toString(16).padStart(32, '0'), "revoked", AccessMode.SAFE, ClientState.REVOKED), ByteArray(32)) }.toMutableList()).use {
            val plain = RecordCodec.encode(it, Rules.bytes(ca, 32)); try { record.writeBytes(protection().encrypt(plain)) } finally { plain.fill(0) }
        }
        error(StoreError.CAPACITY) { s.approve("ff".repeat(16), "overflow", token, 1) }; assertEquals(128, s.snapshot().clients.size)
    }
    @Test fun keystoreIsNonExportableAndNonceChanges() {
        val s = fresh(); assertNull(keyStore().getKey(alias, null).encoded); s.keyProtection()
        val ciphertexts = (1..24).map { protection().encrypt(ByteArray(47)) }
        assertEquals(24, ciphertexts.map { Rules.hex(it.copyOfRange(5, 17)) }.toSet().size)
        assertTrue(ciphertexts.all { it.size == 80 })
    }
    @Test fun incorrectCaBindingPoisonsAllLocalInstances() {
        val (s, _) = active(); error(StoreError.NEEDS_REPAIR) { store(hash = "44".repeat(32)).snapshot() }
        error(StoreError.NEEDS_REPAIR) { s.authenticate(client, token) }
    }
    @Test fun missingDatabaseDoesNotInitialize() {
        val s = fresh(); record.delete(); error(StoreError.NEEDS_REPAIR) { s.snapshot() }
        error(StoreError.NEEDS_REPAIR) { store().initialize() }; assertFalse(record.exists()); assertTrue(keyStore().containsAlias(alias))
    }
    @Test fun missingKeyDoesNotRegenerate() {
        val s = fresh(); val before = record.readBytes(); keyStore().deleteEntry(alias)
        error(StoreError.NEEDS_REPAIR) { s.snapshot() }; assertFalse(keyStore().containsAlias(alias)); assertArrayEquals(before, record.readBytes())
    }
    @Test fun replacementKeyCannotReadOldCiphertext() {
        val s = fresh(); keyStore().deleteEntry(alias); protection().create()
        error(StoreError.NEEDS_REPAIR) { s.snapshot() }
    }
    @Test fun existingKeyOrDatabaseRefusesFreshInitialization() {
        val s = fresh(); error(StoreError.ALREADY_EXISTS) { store().initialize() }; assertEquals(1L, s.snapshot().revision)
    }
    @Test fun deviceProtectedContextIsRefusedBeforeCreatingStore() {
        error(StoreError.LOCKED) { PairingStore.testStore(context.createDeviceProtectedStorageContext(), ca, name) }
        assertFalse(root.exists())
    }
    @Test fun invalidInputsNeverCreateStore() {
        val s = store()
        for (bad in listOf("", "a\nb", "a\u200bb", "字".repeat(129), "\ud800"))
            error(StoreError.INVALID_INPUT) { s.approve(client, bad, token, 1) }
        error(StoreError.INVALID_INPUT) { s.approve("bad", "pc", token, 1) }
        error(StoreError.INVALID_INPUT) { s.approve(client, "pc", ByteArray(31), 1) }
        assertFalse(root.exists())
    }
    @Test fun ciphertextTamperingTruncationAndOversizeFailClosed() {
        for (kind in 0..3) {
            setup(); val s = fresh(); val original = record.readBytes()
            val changed = when (kind) { 0 -> original.copyOf().also { it[it.lastIndex] = (it.last().toInt() xor 1).toByte() }; 1 -> original.copyOf(20); 2 -> ByteArray(65537); else -> ByteArray(0) }
            record.writeBytes(changed); error(StoreError.NEEDS_REPAIR) { s.snapshot() }; assertArrayEquals(changed, record.readBytes())
        }
    }
    @Test fun protectedMalformedRecordCannotAuthorize() {
        for (kind in 0..10) {
            setup(); val (s, _) = active()
            mutate { plain ->
                when (kind) {
                    0 -> plain.also { it[0] = 0 }; 1 -> plain.also { it[4] = 2 }; 2 -> plain.also { it[5] = 0 }
                    3 -> plain.also { java.util.Arrays.fill(it, 37, 45, 0.toByte()) }; 4 -> plain.also { it[46] = 129.toByte() }
                    5 -> plain.also { it[95] = 127 }; 6 -> plain.also { it[96] = 127 }; 7 -> plain.also { it[it.lastIndex] = 255.toByte() }
                    8 -> plain + byteArrayOf(0); 9 -> plain.copyOf(46)
                    else -> (plain + plain.copyOfRange(47, plain.size)).also { it[46] = 2 }
                }
            }
            error(StoreError.NEEDS_REPAIR) { s.authenticate(client, token) }
        }
    }
    @Test fun unsafeModesAndLinksAreRejected() {
        for (kind in listOf(0, 1, 2, 4)) {
            setup(); val s = fresh()
            when (kind) {
                0 -> Os.chmod(root.path, 493); 1 -> Os.chmod(record.path, 420)
                2 -> Os.chmod(File(root, ".lock").path, 420)
                else -> { val saved = File(root, "saved.bin"); Os.rename(record.path, saved.path); Os.symlink(saved.path, record.path) }
            }
            error(StoreError.NEEDS_REPAIR) { s.snapshot() }
        }
    }
    @Test fun symlinkStoreRootRejected() {
        val s = fresh(); val moved = File(context.noBackupFilesDir, name + "-saved"); Os.rename(root.path, moved.path); Os.symlink(moved.path, root.path)
        error(StoreError.NEEDS_REPAIR) { s.snapshot() }
    }
    @Test fun externalFileLockBlocksWithoutPoisoning() {
        val s = fresh(); PrivateFiles.acquire(root, false).use { error(StoreError.BUSY) { s.snapshot() } }
        assertEquals(1L, s.snapshot().revision)
    }
    @Test fun hardlinkCreationIsBlockedByPlatformOrStore() {
        val s = fresh()
        try {
            Os.link(record.path, File(root, "other.bin").path)
            error(StoreError.NEEDS_REPAIR) { s.snapshot() }
            InstrumentationRegistry.getInstrumentation().sendStatus(2, android.os.Bundle().apply { putString("hardlink_coverage", "store_rejected") })
        } catch (e: android.system.ErrnoException) {
            assertTrue(e.errno == android.system.OsConstants.EACCES || e.errno == android.system.OsConstants.EPERM)
            assertEquals(1L, s.snapshot().revision)
            InstrumentationRegistry.getInstrumentation().sendStatus(2, android.os.Bundle().apply { putString("hardlink_coverage", "platform_denied_creation") })
        }
    }
    @Test fun ownedDescriptorsAreAtomicallyCloseOnExec() {
        val s = fresh()
        repeat(20) { s.snapshot() }
        PrivateFiles.acquire(root, false).use {
            val expected = setOf(root.canonicalPath, File(root, ".lock").canonicalPath)
            var found = 0
            for (entry in File("/proc/self/fd").listFiles()!!) {
                val target = try { Os.readlink(entry.path) } catch (_: Exception) { continue }
                if (target !in expected) continue
                val flags = File("/proc/self/fdinfo/${entry.name}").readLines().single { it.startsWith("flags:") }.substringAfter(':').trim().toLong(8)
                assertTrue(flags and 0x80000 != 0L); found++
            }
            assertEquals(2, found)
        }
        val expected = setOf(root.canonicalPath, File(root, ".lock").canonicalPath)
        val remaining = File("/proc/self/fd").listFiles()!!.count {
            val target = try { Os.readlink(it.path) } catch (_: Exception) { "" }
            target in expected
        }
        assertEquals(0, remaining)
    }
    @Test fun failedInitializationNeverBecomesFreshAutomatically() {
        for (stage in CommitStage.entries) {
            setup(); val s = store({ if (it == stage) throw java.io.IOException("synthetic failure") })
            error(StoreError.STORAGE_FAILURE) { s.initialize() }
            error(StoreError.NEEDS_REPAIR) { store().snapshot() }
            error(StoreError.NEEDS_REPAIR) { store().initialize() }; assertTrue(keyStore().containsAlias(alias))
        }
    }
    @Test fun failedRevocationPoisonsAllInstancesAndLeavesObservableDiskState() {
        for (stage in CommitStage.entries.filter { it != CommitStage.KEY_CREATED }) {
            setup(); val (old, snap) = active()
            val failing = store({ if (it == stage) throw java.io.IOException("synthetic failure") })
            error(StoreError.STORAGE_FAILURE) { failing.revoke(client, snap.revision) }
            error(StoreError.NEEDS_REPAIR) { old.authenticate(client, token) }; error(StoreError.NEEDS_REPAIR) { store().snapshot() }
            val plain = protection().decrypt(record.readBytes())
            try { RecordCodec.decode(plain, Rules.bytes(ca, 32)).use { db ->
                assertEquals(if (stage >= CommitStage.RENAMED) ClientState.REVOKED else ClientState.ACTIVE, db.entries.single().client.state)
            } } finally { plain.fill(0) }
        }
    }
    @Test fun revokeAndAuthorizedCommitShareOneBarrier() {
        val (s, snap) = active(); val entered = CountDownLatch(1); val release = CountDownLatch(1); val revoked = CountDownLatch(1)
        val problem = AtomicReference<Throwable?>()
        val writer = Thread { try { s.withAuthorizedCommit(client, token) { entered.countDown(); check(release.await(5, TimeUnit.SECONDS)) } } catch (e: Throwable) { problem.set(e) } }
        val revoker = Thread { try { store().revoke(client, snap.revision); revoked.countDown() } catch (e: Throwable) { problem.set(e) } }
        writer.start()
        try { assertTrue(entered.await(5, TimeUnit.SECONDS)); revoker.start(); assertFalse(revoked.await(100, TimeUnit.MILLISECONDS)) }
        finally { release.countDown(); writer.join(5000); if (revoker.state != Thread.State.NEW) revoker.join(5000) }
        assertFalse(writer.isAlive); assertFalse(revoker.isAlive); assertNull(problem.get()); assertEquals(0L, revoked.count)
        var called = false; error(StoreError.UNAUTHORIZED) { s.withAuthorizedCommit(client, token) { called = true } }; assertFalse(called)
    }
    private fun ByteArray.indexOfSlice(needle: ByteArray): Int {
        for (i in 0..size - needle.size) if (needle.indices.all { this[i + it] == needle[it] }) return i
        return -1
    }
}
