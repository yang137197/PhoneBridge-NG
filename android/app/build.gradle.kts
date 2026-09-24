plugins { id("com.android.application") }
android {
    namespace = "org.phonebridge.ng"
    compileSdk = 36
    buildToolsVersion = "36.0.0"
    defaultConfig {
        applicationId = "org.phonebridge.ng"
        minSdk = 26; targetSdk = 36
        versionCode = 1; versionName = "0.1.0"
        testInstrumentationRunner = "org.phonebridge.ng.BridgeTestRunner"
    }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_1_8; targetCompatibility = JavaVersion.VERSION_1_8 }
    sourceSets.named("main") {
        kotlin.directories += listOf("../pairing-core/src/main/kotlin", "../credentials-store/src/main/java")
        res.directories += "../credentials-store/src/main/res"
    }
    sourceSets.named("test") { kotlin.directories += "../pairing-core/src/test/kotlin" }
    sourceSets.named("androidTest") { kotlin.directories += "../credentials-store/src/androidTest/java" }
    lint {
        abortOnError = true; warningsAsErrors = true
        disable += setOf("AndroidGradlePluginVersion", "GradleDependency")
    }
    packaging {
        resources.excludes += setOf("META-INF/versions/9/OSGI-INF/MANIFEST.MF")
        resources.merges += "META-INF/LICENSE.md"
    }
}
kotlin { compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_1_8); allWarningsAsErrors.set(true) } }
dependencies {
    implementation("org.bouncycastle:bcprov-jdk18on:1.86")
    implementation("org.bouncycastle:bcpkix-jdk18on:1.86")
    testImplementation("junit:junit:4.13.2")
    androidTestImplementation("androidx.test.ext:junit:1.1.5")
    androidTestImplementation("androidx.test:runner:1.5.2")
}
dependencyLocking { lockAllConfigurations(); lockMode.set(LockMode.STRICT) }
