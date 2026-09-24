package org.phonebridge.credentials

import android.os.Build
import android.system.ErrnoException
import android.system.Os
import android.system.OsConstants
import java.io.File
import java.io.FileDescriptor
import java.io.FileOutputStream
import java.nio.channels.FileLock
import java.util.UUID

internal enum class CommitStage { KEY_CREATED, ENCRYPTED, TEMP_CREATED, FLUSHED, RENAMED, DIRECTORY_SYNCED }
internal class PrivateFiles private constructor(private val root: File, private val directory: FileDescriptor,
    private val lockDescriptor: FileDescriptor, private val stream: FileOutputStream, private val lock: FileLock) : AutoCloseable {
    private var closed = false
    companion object {
        fun acquire(root: File, create: Boolean): PrivateFiles {
            if (!root.exists()) {
                if (!create) throw StoreException(StoreError.NEEDS_REPAIR)
                Os.mkdir(root.path, 448)
            }
            val dir = openDescriptor(root, OsConstants.O_RDONLY, 0)
            try {
                check(dir, true)
                val fd = open(File(root, ".lock"), OsConstants.O_CREAT or OsConstants.O_RDWR)
                val stream = FileOutputStream(fd)
                try {
                    val lock = try { stream.channel.tryLock() } catch (_: java.nio.channels.OverlappingFileLockException) { null }
                    if (lock == null) throw StoreException(StoreError.BUSY)
                    return PrivateFiles(root, dir, fd, stream, lock)
                } catch (e: Exception) {
                    try { stream.close() } finally { if (fd.valid()) Os.close(fd) }
                    throw e
                }
            } catch (e: Exception) { Os.close(dir); throw e }
        }
        private fun check(fd: FileDescriptor, directory: Boolean) {
            val stat = Os.fstat(fd)
            val type = if (directory) OsConstants.S_IFDIR else OsConstants.S_IFREG
            val mode = if (directory) 448 else 384
            if (stat.st_uid != Os.getuid() || stat.st_mode and OsConstants.S_IFMT != type || stat.st_mode and 4095 != mode ||
                (!directory && stat.st_nlink != 1L)) throw StoreException(StoreError.NEEDS_REPAIR)
        }
        private fun open(file: File, flags: Int): FileDescriptor {
            val fd = openDescriptor(file, flags, 384)
            try { check(fd, false); return fd } catch (e: Exception) { Os.close(fd); throw e }
        }
        private fun openDescriptor(file: File, flags: Int, mode: Int): FileDescriptor {
            // API 26 lacks the Java field. AOSP android-8.0.0_r1 bionic asm-generic/fcntl.h defines
            // O_CLOEXEC as octal 02000000. Pass the public open flag directly; no hidden API/reflection.
            val atomicClose = if (Build.VERSION.SDK_INT >= 27) OsConstants.O_CLOEXEC else 0x80000
            return Os.open(file.path, flags or OsConstants.O_NOFOLLOW or atomicClose, mode)
        }
    }
    fun exists(): Boolean = try { Os.lstat(File(root, "records.bin").path); true } catch (e: ErrnoException) {
        if (e.errno == OsConstants.ENOENT) false else throw e
    }
    fun read(): ByteArray {
        val fd = open(File(root, "records.bin"), OsConstants.O_RDONLY)
        try {
            val length = Os.fstat(fd).st_size; if (length !in 80..Rules.MAX_FILE.toLong()) throw StoreException(StoreError.NEEDS_REPAIR)
            val result = ByteArray(length.toInt()); var position = 0
            while (position < result.size) { val n = Os.read(fd, result, position, result.size - position); if (n <= 0) throw StoreException(StoreError.NEEDS_REPAIR); position += n }
            if (Os.read(fd, ByteArray(1), 0, 1) != 0) throw StoreException(StoreError.NEEDS_REPAIR)
            return result
        } finally { Os.close(fd) }
    }
    fun write(ciphertext: ByteArray, checkpoint: ((CommitStage) -> Unit)?) {
        require(ciphertext.size in 80..Rules.MAX_FILE)
        val temporary = File(root, ".pending-${UUID.randomUUID()}.bin")
        var created = false
        try {
            val fd = open(temporary, OsConstants.O_WRONLY or OsConstants.O_CREAT or OsConstants.O_EXCL); created = true
            try {
                checkpoint?.invoke(CommitStage.TEMP_CREATED)
                var position = 0
                while (position < ciphertext.size) { val n = Os.write(fd, ciphertext, position, ciphertext.size - position); if (n <= 0) throw java.io.IOException(); position += n }
                fd.sync(); checkpoint?.invoke(CommitStage.FLUSHED)
            } finally { Os.close(fd) }
            Os.rename(temporary.path, File(root, "records.bin").path); checkpoint?.invoke(CommitStage.RENAMED)
            Os.fsync(directory); checkpoint?.invoke(CommitStage.DIRECTORY_SYNCED)
        } finally {
            if (created) try { Os.remove(temporary.path) } catch (_: ErrnoException) { /* A leftover is encrypted and never loaded as a record. */ }
        }
    }
    override fun close() {
        if (closed) return
        closed = true
        try { lock.release() } finally {
            try { stream.close() } finally {
                // Android's public FileOutputStream(fd) constructor does not own the supplied descriptor.
                try { if (lockDescriptor.valid()) Os.close(lockDescriptor) } finally { Os.close(directory) }
            }
        }
    }
}
