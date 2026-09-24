package org.phonebridge.ng

import android.content.Context
import android.content.res.Configuration
import java.util.Locale

internal object AppLanguage {
    private const val PREFERENCES = "settings"
    private const val KEY = "language"
    const val CHINESE = "zh-CN"
    const val ENGLISH = "en-US"

    fun selected(context: Context): String = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
        .getString(KEY, CHINESE).takeIf { it == CHINESE || it == ENGLISH } ?: CHINESE

    fun set(context: Context, language: String): Boolean {
        require(language == CHINESE || language == ENGLISH)
        if (selected(context) == language) return false
        context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).edit().putString(KEY, language).apply()
        return true
    }

    fun wrap(context: Context): Context {
        val locale = Locale.forLanguageTag(selected(context))
        Locale.setDefault(locale)
        val configuration = Configuration(context.resources.configuration)
        configuration.setLocale(locale)
        configuration.setLayoutDirection(locale)
        return context.createConfigurationContext(configuration)
    }
}
