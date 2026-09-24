plugins { kotlin("jvm") version "2.4.0" }

dependencies {
    implementation("org.bouncycastle:bcprov-jdk18on:1.86")
    testImplementation("junit:junit:4.13.2")
}
java {
    sourceCompatibility = JavaVersion.VERSION_1_8
    targetCompatibility = JavaVersion.VERSION_1_8
}
kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_1_8)
        freeCompilerArgs.add("-Xjdk-release=8")
        allWarningsAsErrors.set(true)
    }
}
dependencyLocking { lockAllConfigurations(); lockMode.set(LockMode.STRICT) }
tasks.register("writeTestClasspath") {
    val classpath = sourceSets.test.get().runtimeClasspath
    val output = layout.buildDirectory.file("test-classpath.txt")
    outputs.file(output)
    doLast { output.get().asFile.writeText(classpath.asPath) }
}
