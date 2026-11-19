pluginManagement {
    repositories {
        google()
        mavenCentral()
        // Google's copy of Maven Central, for when Maven Central answers 404 for a file it has.
        maven("https://maven-central.storage-download.googleapis.com/maven2/")
        gradlePluginPortal()
    }
    // Versions are set here and applied per module, so building :protocol
    // never has to download the Android Gradle Plugin.
    plugins {
        id("com.android.application") version "8.13.0"
        id("org.jetbrains.kotlin.android") version "2.2.21"
        id("org.jetbrains.kotlin.jvm") version "2.2.21"
        id("org.jetbrains.kotlin.plugin.compose") version "2.2.21"
    }
}

dependencyResolutionManagement {
    repositories {
        google()
        mavenCentral()
        // Google's copy of Maven Central, for when Maven Central answers 404 for a file it has.
        maven("https://maven-central.storage-download.googleapis.com/maven2/")
    }
}

rootProject.name = "flyylink"

include(":protocol")

// The app module needs the Android SDK. The protocol module is plain Kotlin,
// so it can still be built and tested on machines without the SDK.
val localProps = file("local.properties")
val hasSdk = System.getenv("ANDROID_HOME") != null ||
    System.getenv("ANDROID_SDK_ROOT") != null ||
    (localProps.exists() && localProps.readText().contains("sdk.dir"))

if (hasSdk) {
    include(":app")
} else {
    logger.warn("Android SDK not found: skipping :app. Set ANDROID_HOME or sdk.dir in local.properties.")
}
