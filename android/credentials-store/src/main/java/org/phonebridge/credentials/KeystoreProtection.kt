package org.phonebridge.credentials

import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyInfo
import android.security.keystore.KeyProperties
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.GCMParameterSpec

class KeyProtectionInfo internal constructor(val securityLevel: Int?, val insideSecureHardware: Boolean)
internal class KeystoreProtection(private val alias: String, private val ca: ByteArray) {
    private fun store() = KeyStore.getInstance("AndroidKeyStore").also { it.load(null) }
    fun exists(): Boolean = store().containsAlias(alias)
    fun create() {
        if (exists()) throw StoreException(StoreError.ALREADY_EXISTS)
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setKeySize(256).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true).setUserAuthenticationRequired(false).build())
        generator.generateKey()
    }
    private fun key(): SecretKey {
        val key = store().getKey(alias, null) as? SecretKey ?: throw StoreException(StoreError.NEEDS_REPAIR)
        val info = info(key)
        if (key.encoded != null || info.keySize != 256 || info.isUserAuthenticationRequired ||
            !info.blockModes.contentEquals(arrayOf(KeyProperties.BLOCK_MODE_GCM)) ||
            !info.encryptionPaddings.contentEquals(arrayOf(KeyProperties.ENCRYPTION_PADDING_NONE)) ||
            info.purposes != KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            throw StoreException(StoreError.NEEDS_REPAIR)
        return key
    }
    private fun info(key: SecretKey): KeyInfo = SecretKeyFactory.getInstance("AES", "AndroidKeyStore").getKeySpec(key, KeyInfo::class.java) as KeyInfo
    @Suppress("DEPRECATION")
    fun protectionInfo(): KeyProtectionInfo {
        val info = info(key())
        return KeyProtectionInfo(if (Build.VERSION.SDK_INT >= 31) info.securityLevel else null, info.isInsideSecureHardware)
    }
    private fun aad() = "PhoneBridge NG|android-records|v1".toByteArray(Charsets.US_ASCII) + byteArrayOf(0) + ca
    fun encrypt(plaintext: ByteArray): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key()); cipher.updateAAD(aad())
        val nonce = cipher.iv; require(nonce.size == 12)
        return "PBE1".toByteArray(Charsets.US_ASCII) + byteArrayOf(1) + nonce + cipher.doFinal(plaintext)
    }
    fun decrypt(blob: ByteArray): ByteArray {
        try {
            require(blob.size in 80..Rules.MAX_FILE && blob.copyOfRange(0, 5).contentEquals(byteArrayOf(80, 66, 69, 49, 1)))
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, blob, 5, 12)); cipher.updateAAD(aad())
            return cipher.doFinal(blob, 17, blob.size - 17)
        } catch (_: Exception) { throw StoreException(StoreError.NEEDS_REPAIR) }
    }
}
