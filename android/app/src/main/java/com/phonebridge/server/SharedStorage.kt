package com.phonebridge.server

import android.os.ParcelFileDescriptor
import android.system.ErrnoException
import android.system.Os
import android.system.OsConstants.*
import android.system.StructStat
import java.io.*
import java.security.MessageDigest
import java.util.UUID

// PhoneBridge NG, 2026-09-19. SPDX-License-Identifier: GPL-3.0-or-later
internal data class SharedEntry(val path: SharedPath, val directory: Boolean, val size: Long, val modified: Long)
internal data class DeleteSnapshot(val entry: SharedEntry, internal val digest: ByteArray)
internal typealias StorageCommit = ((() -> Unit) -> Unit)

/**
 * All names are validated single components below held directory descriptors. Public Android Os
 * APIs do not expose openat/mkdirat; /proc/self/fd provides the descriptor-relative parent on Android.
 * Every untrusted component is opened O_NOFOLLOW. Unsupported filesystems fail closed.
 * Operations are serialized, including upload commit; open GET streams retain independent file FDs.
 * External apps can still edit ordinary files. This is not a transaction across other apps or power loss.
 */
internal class SharedStorage(rootDir: File) : Closeable {
    // Android's trusted external-storage prefix contains platform aliases; reject a symlink at
    // the selected folder itself instead of resolving it to a different shared root.
    private val root = openDirectory(File(rootDir.absoluteFile.parentFile!!.canonicalFile, rootDir.name).path)
    private var closed = false

    @Synchronized override fun close() {
        if (!closed) { closed = true; root.close() }
    }

    private fun fdPath(fd: ParcelFileDescriptor) = "/proc/self/fd/${fd.fd}"
    private fun child(fd: ParcelFileDescriptor, name: String) = "${fdPath(fd)}/$name"

    private fun open(path: String, flags: Int, mode: Int = 0): ParcelFileDescriptor {
        // The public O_CLOEXEC constant starts at API 27; do not access it on API 26.
        val closeOnExec = if (android.os.Build.VERSION.SDK_INT >= 27) O_CLOEXEC else 0
        val fd = Os.open(path, flags or O_NOFOLLOW or closeOnExec or O_NONBLOCK, mode)
        try { return ParcelFileDescriptor.dup(fd) } finally { Os.close(fd) }
    }

    private fun openDirectory(path: String): ParcelFileDescriptor {
        val handle = open(path, O_RDONLY)
        try {
            if (!S_ISDIR(Os.fstat(handle.fileDescriptor).st_mode)) {
                throw StorageFailure(403, "A directory is required")
            }
            return handle
        } catch (error: Exception) { handle.close(); throw error }
    }

    private fun directory(parts: List<String>): ParcelFileDescriptor {
        check(!closed) { "Storage closed" }
        var current = ParcelFileDescriptor.dup(root.fileDescriptor)
        try {
            for (name in parts) {
                val next = openDirectory(child(current, name))
                current.close()
                current = next
            }
            return current
        } catch (error: Exception) { current.close(); throw error }
    }

    private fun statOrNull(path: String): StructStat? = try { Os.lstat(path) } catch (error: ErrnoException) {
        if (error.errno == ENOENT) null else throw error
    }

    private fun regularOrDirectory(stat: StructStat) {
        if (!S_ISDIR(stat.st_mode) && (!S_ISREG(stat.st_mode) || stat.st_nlink != 1L)) {
            throw StorageFailure(403, "Links and special files are not shared")
        }
    }

    private fun required(path: String): StructStat = (statOrNull(path)
        ?: throw StorageFailure(404, "Not found")).also { regularOrDirectory(it) }

    private fun entry(path: SharedPath, stat: StructStat) =
        SharedEntry(path, S_ISDIR(stat.st_mode), stat.st_size, stat.st_mtime * 1000)

    @Synchronized fun info(path: SharedPath): SharedEntry {
        if (path.isRoot) return entry(path, Os.fstat(root.fileDescriptor))
        return directory(path.parts.dropLast(1)).use { entry(path, required(child(it, path.name))) }
    }

    @Synchronized fun list(path: SharedPath): List<SharedEntry> = directory(path.parts).use { dir ->
        val names = File(fdPath(dir)).list() ?: throw IOException("Cannot list directory")
        names.sorted().mapNotNull { name ->
            if (name.startsWith(SharedPath.STAGING_PREFIX)) return@mapNotNull null
            val item = try { path.child(name) } catch (_: StorageFailure) { return@mapNotNull null }
            val stat = statOrNull(child(dir, name)) ?: return@mapNotNull null
            try { regularOrDirectory(stat) } catch (_: StorageFailure) { return@mapNotNull null }
            entry(item, stat)
        }
    }

    @Synchronized fun read(path: SharedPath): FileInputStream {
        path.requireChild()
        return directory(path.parts.dropLast(1)).use { parent -> readFile(child(parent, path.name)) }
    }

    private fun readFile(path: String): FileInputStream {
        val handle = open(path, O_RDONLY)
        try {
            val stat = Os.fstat(handle.fileDescriptor)
            regularOrDirectory(stat)
            if (!S_ISREG(stat.st_mode)) throw StorageFailure(409, "A regular file is required")
            return ParcelFileDescriptor.AutoCloseInputStream(handle)
        } catch (error: Exception) { handle.close(); throw error }
    }

    private fun snapshot(path: String): StructStat? = statOrNull(path)?.also { regularOrDirectory(it) }
    private fun same(left: StructStat?, right: StructStat?): Boolean =
        if (left == null || right == null) left == right else
            left.st_dev == right.st_dev && left.st_ino == right.st_ino && left.st_mode == right.st_mode &&
                left.st_size == right.st_size && left.st_mtime == right.st_mtime && left.st_ctime == right.st_ctime

    private fun sameInode(left: StructStat, right: StructStat?) = right != null &&
        left.st_dev == right.st_dev && left.st_ino == right.st_ino && left.st_mode == right.st_mode

    private fun unchanged(path: String, before: StructStat?) {
        if (!same(before, snapshot(path))) throw StorageFailure(409, "Destination changed during transfer")
    }

    /** No O_TRUNC on the destination. Short bodies and I/O failures leave the old file intact. */
    @Synchronized fun put(path: SharedPath, input: InputStream, length: Long, overwrite: Boolean = true,
                          received: (Long) -> Unit = {}, commit: StorageCommit = { it() }): Boolean {
        path.requireChild()
        require(length >= 0)
        return directory(path.parts.dropLast(1)).use { parent ->
            val destination = child(parent, path.name)
            val before = snapshot(destination)
            if (before != null && S_ISDIR(before.st_mode)) throw StorageFailure(409, "Cannot replace a directory with a file")
            checkOverwrite(before, overwrite)
            stageFile(parent, destination, before, input, length, received, commit)
            before == null
        }
    }

    private fun stageFile(parent: ParcelFileDescriptor, destination: String, before: StructStat?,
                          input: InputStream, length: Long, received: (Long) -> Unit = {},
                          commit: StorageCommit = { it() }) {
        val temp = child(parent, SharedPath.STAGING_PREFIX + UUID.randomUUID() + ".part")
        var owned: StructStat? = null
        try {
            val output = open(temp, O_WRONLY or O_CREAT or O_EXCL, 384) // 0600
            ParcelFileDescriptor.AutoCloseOutputStream(output).use { stream ->
                owned = Os.fstat(stream.fd)
                copyExact(input, stream, length, received)
                stream.flush()
                Os.fsync(stream.fd)
                commit {
                    unchanged(destination, before)
                    // Commit only the inode we wrote, even if another local app replaced the temp name.
                    if (!same(Os.fstat(stream.fd), snapshot(temp))) throw StorageFailure(409, "Staging file changed")
                    Os.rename(temp, destination)
                }
                owned = null
            }
        } finally {
            // Only our freshly-created temporary name; no wildcard/startup cleanup or unknown cache deletion.
            owned?.let { original ->
                try { if (sameInode(original, statOrNull(temp))) Os.remove(temp) }
                catch (_: Exception) { /* Retain on failure for diagnosis. */ }
            }
        }
    }

    private fun copyExact(input: InputStream, output: OutputStream, length: Long, received: (Long) -> Unit) {
        val buffer = ByteArray(256 * 1024)
        var remaining = length
        while (remaining > 0) {
            val count = input.read(buffer, 0, minOf(remaining, buffer.size.toLong()).toInt())
            if (count < 0) throw EOFException("Incomplete upload")
            if (count == 0) throw IOException("Upload made no progress")
            output.write(buffer, 0, count)
            remaining -= count
            received(count.toLong())
        }
    }

    @Synchronized fun mkdir(path: SharedPath, commit: StorageCommit = { it() }) {
        path.requireChild()
        directory(path.parts.dropLast(1)).use { parent ->
            val target = child(parent, path.name)
            if (statOrNull(target) != null) throw StorageFailure(405, "Already exists")
            commit {
                if (statOrNull(target) != null) throw StorageFailure(409, "Destination changed")
                Os.mkdir(target, 448) // 0700; no implicit parent creation.
            }
        }
    }

    @Synchronized fun delete(path: SharedPath, commit: StorageCommit = { it() }) {
        path.requireChild()
        directory(path.parts.dropLast(1)).use { parent ->
            inspectTree(parent, path.name, 0)
            commit { inspectTree(parent, path.name, 0); deleteTree(parent, path.name, 0) }
        }
    }

    @Synchronized fun prepareDelete(path: SharedPath): DeleteSnapshot {
        path.requireChild()
        return directory(path.parts.dropLast(1)).use { parent ->
            val stat = required(child(parent, path.name))
            DeleteSnapshot(entry(path, stat), treeDigest(parent, path.name, 0))
        }
    }

    @Synchronized fun deleteConfirmed(snapshot: DeleteSnapshot, commit: StorageCommit = { it() }) {
        val path = snapshot.entry.path
        path.requireChild()
        directory(path.parts.dropLast(1)).use { parent ->
            if (!snapshot.digest.contentEquals(treeDigest(parent, path.name, 0))) {
                throw StorageFailure(409, "Target changed after confirmation")
            }
            commit {
                if (!snapshot.digest.contentEquals(treeDigest(parent, path.name, 0))) {
                    throw StorageFailure(409, "Target changed after confirmation")
                }
                deleteTree(parent, path.name, 0)
            }
        }
    }

    private fun names(dir: ParcelFileDescriptor): Array<String> =
        File(fdPath(dir)).list() ?: throw IOException("Cannot list directory")

    private fun inspectTree(parent: ParcelFileDescriptor, name: String, depth: Int) {
        if (depth > 128) throw StorageFailure(409, "Directory tree is too deep")
        if (name.startsWith(SharedPath.STAGING_PREFIX)) throw StorageFailure(409, "Directory contains retained staging data")
        val stat = required(child(parent, name))
        if (S_ISDIR(stat.st_mode)) openDirectory(child(parent, name)).use { dir ->
            for (item in names(dir)) inspectTree(dir, item, depth + 1)
        }
    }

    private fun treeDigest(parent: ParcelFileDescriptor, name: String, depth: Int): ByteArray {
        val digest = MessageDigest.getInstance("SHA-256")
        fun visit(dir: ParcelFileDescriptor, item: String, level: Int) {
            if (level > 128) throw StorageFailure(409, "Directory tree is too deep")
            if (item.startsWith(SharedPath.STAGING_PREFIX)) throw StorageFailure(409, "Directory contains retained staging data")
            val stat = required(child(dir, item))
            val encoded = item.toByteArray(Charsets.UTF_8)
            digest.update(byteArrayOf((encoded.size ushr 8).toByte(), encoded.size.toByte()))
            digest.update(encoded)
            for (value in longArrayOf(stat.st_dev, stat.st_ino, stat.st_mode.toLong(), stat.st_size, stat.st_mtime, stat.st_ctime)) {
                for (shift in 56 downTo 0 step 8) digest.update((value ushr shift).toByte())
            }
            if (S_ISDIR(stat.st_mode)) openDirectory(child(dir, item)).use { nested ->
                for (nestedName in names(nested).sorted()) visit(nested, nestedName, level + 1)
            }
        }
        visit(parent, name, depth)
        return digest.digest()
    }

    private fun deleteTree(parent: ParcelFileDescriptor, name: String, depth: Int) {
        if (depth > 128) throw StorageFailure(409, "Directory tree is too deep")
        val target = child(parent, name)
        val stat = required(target)
        if (S_ISDIR(stat.st_mode)) {
            openDirectory(target).use { dir ->
                for (item in names(dir)) {
                    if (item.startsWith(SharedPath.STAGING_PREFIX)) throw StorageFailure(409, "Retained staging data")
                    deleteTree(dir, item, depth + 1)
                }
            }
            Os.remove(target)
        } else Os.remove(target)
    }

    private fun transferPaths(source: SharedPath, dest: SharedPath) {
        source.requireChild()
        dest.requireChild()
        if (source == dest || dest.parts.take(source.parts.size) == source.parts ||
            source.parts.take(dest.parts.size) == dest.parts) {
            throw StorageFailure(403, "Source and Destination overlap")
        }
    }

    private fun checkOverwrite(before: StructStat?, overwrite: Boolean) {
        if (before != null && !overwrite) throw StorageFailure(412, "Destination already exists")
        if (before != null && S_ISDIR(before.st_mode)) {
            throw StorageFailure(409, "Replacing an existing collection is not supported")
        }
    }

    @Synchronized fun move(source: SharedPath, dest: SharedPath, overwrite: Boolean,
                           commit: StorageCommit = { it() }): Boolean {
        transferPaths(source, dest)
        return directory(source.parts.dropLast(1)).use { from ->
            directory(dest.parts.dropLast(1)).use { to ->
                inspectTree(from, source.name, 0)
                val sourceBefore = snapshot(child(from, source.name))
                val target = child(to, dest.name)
                val before = snapshot(target)
                checkOverwrite(before, overwrite)
                commit {
                    unchanged(child(from, source.name), sourceBefore)
                    unchanged(target, before)
                    Os.rename(child(from, source.name), target)
                }
                before == null
            }
        }
    }

    @Synchronized fun copy(source: SharedPath, dest: SharedPath, overwrite: Boolean,
                           commit: StorageCommit = { it() }): Boolean {
        transferPaths(source, dest)
        return directory(source.parts.dropLast(1)).use { from ->
            directory(dest.parts.dropLast(1)).use { to ->
                inspectTree(from, source.name, 0)
                val target = child(to, dest.name)
                val before = snapshot(target)
                checkOverwrite(before, overwrite)
                val stat = required(child(from, source.name))
                if (!S_ISDIR(stat.st_mode)) {
                    readFile(child(from, source.name)).use { input ->
                        stageFile(to, target, before, input, input.channel.size(), commit = commit)
                    }
                } else {
                    if (before != null) throw StorageFailure(409, "Cannot replace a file with a directory")
                    val tempName = SharedPath.STAGING_PREFIX + UUID.randomUUID() + ".part"
                    val temp = child(to, tempName)
                    Os.mkdir(temp, 448)
                    try {
                        openDirectory(child(from, source.name)).use { src ->
                            openDirectory(temp).use { dst ->
                                copyTree(src, dst, 0)
                                commit {
                                    unchanged(target, before)
                                    if (!sameInode(Os.fstat(dst.fileDescriptor), statOrNull(temp))) {
                                        throw StorageFailure(409, "Staging directory changed")
                                    }
                                    Os.rename(temp, target)
                                }
                            }
                        }
                    } catch (error: Exception) {
                        // Failed directory copies remain hidden for recovery; never recursively erase unknown data.
                        throw error
                    }
                }
                before == null
            }
        }
    }

    private fun copyTree(from: ParcelFileDescriptor, to: ParcelFileDescriptor, depth: Int) {
        if (depth > 128) throw StorageFailure(409, "Directory tree is too deep")
        for (name in names(from)) {
            if (name.startsWith(SharedPath.STAGING_PREFIX)) throw StorageFailure(409, "Retained staging data")
            val source = child(from, name)
            val stat = required(source)
            val target = child(to, name)
            if (S_ISDIR(stat.st_mode)) {
                Os.mkdir(target, 448)
                openDirectory(source).use { src ->
                    openDirectory(target).use { dst -> copyTree(src, dst, depth + 1) }
                }
            } else readFile(source).use { input -> stageFile(to, target, null, input, input.channel.size()) }
        }
    }
}
