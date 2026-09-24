plugins { id("com.android.library") version "9.1.1" }
android {
    namespace = "org.phonebridge.credentials"
    compileSdk = 36
    buildToolsVersion = "36.0.0"
    defaultConfig { minSdk = 26; testInstrumentationRunner = "org.phonebridge.credentials.StorageTestRunner" }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_1_8; targetCompatibility = JavaVersion.VERSION_1_8 }
    lint {
        abortOnError = true; warningsAsErrors = true
        // Versions are deliberately pinned to the verified toolchain; API/security checks remain enabled.
        disable += setOf("AndroidGradlePluginVersion", "GradleDependency")
    }
}
kotlin { compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_1_8); allWarningsAsErrors.set(true) } }
dependencies { androidTestImplementation("androidx.test.ext:junit:1.1.5"); androidTestImplementation("androidx.test:runner:1.5.2") }
dependencyLocking { lockAllConfigurations(); lockMode.set(LockMode.STRICT) }
