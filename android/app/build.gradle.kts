plugins { id("com.android.application") }
val uiPreviewRevision = providers.gradleProperty("uiPreviewRevision").orElse("dev").get().also {
    require(it.matches(Regex("[a-z][a-z0-9]{0,15}"))) { "uiPreviewRevision must be a short lowercase identifier such as r8" }
}
android {
    namespace = "org.phonebridge.ng"
    compileSdk = 36
    buildToolsVersion = "36.0.0"
    defaultConfig {
        applicationId = "org.phonebridge.ng"
        minSdk = 26; targetSdk = 36
        versionCode = 2; versionName = "0.2.0"
        testInstrumentationRunner = "org.phonebridge.ng.BridgeTestRunner"
    }
    buildTypes {
        create("uiPreview") {
            initWith(getByName("debug"))
            applicationIdSuffix = ".uipreview$uiPreviewRevision"
            versionNameSuffix = "-ui-preview-$uiPreviewRevision"
            isDebuggable = true
            signingConfig = signingConfigs.getByName("debug")
        }
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
    bundle { language { enableSplit = false } }
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
