import groovy.json.JsonSlurper
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

/**
 * The app's version: the base in version.json at the repo root, raised by each release.
 * A release passes -PbuildNumber (added to the base build number) and -PversionName.
 */
class AppVersion(baseFile: File, buildNumber: String?, name: String?) {
    private val base = (JsonSlurper().parse(baseFile) as Map<*, *>)["android"] as Map<*, *>
    private val baseCode = (base["versionCode"] as Number).toInt()
    private val baseName = base["versionName"] as String

    val code = baseCode + (buildNumber?.toInt() ?: 0)
    val name = name ?: baseName

    init {
        require(compare(this.name, baseName) >= 0) {
            "Version ${this.name} is below the base $baseName in version.json."
        }
    }

    private fun compare(a: String, b: String): Int {
        val left = a.substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 }
        val right = b.substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 }

        return (0 until maxOf(left.size, right.size))
            .map { (left.getOrNull(it) ?: 0).compareTo(right.getOrNull(it) ?: 0) }
            .firstOrNull { it != 0 } ?: 0
    }
}

val appVersion = AppVersion(
    rootProject.file("../version.json"),
    findProperty("buildNumber") as String?,
    findProperty("versionName") as String?,
)

android {
    namespace = "com.toneflix.flyylink"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.toneflix.flyylink"
        minSdk = 26
        targetSdk = 36
        versionCode = appVersion.code
        versionName = appVersion.name
    }

    // Release builds are signed only when the keystore is passed in, as the release workflow does.
    val keystore = System.getenv("ANDROID_KEYSTORE_FILE")
    if (keystore != null) {
        signingConfigs {
            create("release") {
                storeFile = file(keystore)
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS")
                keyPassword = System.getenv("ANDROID_KEY_PASSWORD")?.ifEmpty { null } ?: storePassword
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            if (keystore != null) {
                signingConfig = signingConfigs.getByName("release")
            }
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
    }

    packaging {
        // BouncyCastle jars each ship the same OSGi manifest.
        resources.excludes += "META-INF/versions/9/OSGI-INF/MANIFEST.MF"
    }
}

dependencies {
    implementation(project(":protocol"))

    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.core:core-splashscreen:1.0.1")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    implementation(platform("androidx.compose:compose-bom:2024.12.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
}

kotlin {
    compilerOptions { jvmTarget.set(JvmTarget.JVM_17) }
}
