plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.ksp)
    alias(libs.plugins.hilt)
}

android {
    namespace = "com.ridedeck.app"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.ridedeck.app"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "1.0.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"

        // Optional Google Maps SDK key: from the MAPS_API_KEY environment variable (a CI secret) or
        // a mapsApiKey Gradle property. Without it the app uses OpenStreetMap only.
        val mapsApiKey = System.getenv("MAPS_API_KEY")?.takeIf { it.isNotBlank() }
            ?: (project.findProperty("mapsApiKey") as String?).orEmpty()
        manifestPlaceholders["MAPS_API_KEY"] = mapsApiKey
        buildConfigField("String", "MAPS_API_KEY", "\"$mapsApiKey\"")
        vectorDrawables { useSupportLibrary = true }
    }

    signingConfigs {
        // Release builds use a key from environment variables (CI secrets) when provided. Otherwise
        // they fall back to the shared CI key in ci/, so every APK built by the workflow is signed
        // with the same key and installs as an update over the previous one.
        create("release") {
            val envStore = System.getenv("RIDEDECK_KEYSTORE")?.let { file(it) }?.takeIf { it.exists() }
            if (envStore != null) {
                storeFile = envStore
                storePassword = System.getenv("RIDEDECK_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("RIDEDECK_KEY_ALIAS")
                keyPassword = System.getenv("RIDEDECK_KEY_PASSWORD")
            } else {
                storeFile = rootProject.file("ci/ridedeck-ci.jks")
                storePassword = "ridedeck"
                keyAlias = "ridedeck"
                keyPassword = "ridedeck"
            }
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.getByName("release")
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    packaging {
        resources.excludes += "/META-INF/{AL2.0,LGPL2.1}"
    }

    lint {
        // Keep CI green on lint warnings; real errors still fail the build.
        abortOnError = false
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

ksp {
    arg("room.schemaLocation", "$projectDir/schemas")
}

dependencies {
    implementation(project(":core"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.lifecycle.service)
    implementation(libs.androidx.lifecycle.process)
    implementation(libs.androidx.navigation.compose)
    implementation(libs.androidx.datastore.preferences)

    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.material.icons.extended)
    debugImplementation(libs.androidx.compose.ui.tooling)

    implementation(libs.androidx.room.runtime)
    implementation(libs.androidx.room.ktx)
    ksp(libs.androidx.room.compiler)

    implementation(libs.hilt.android)
    ksp(libs.hilt.compiler)
    implementation(libs.hilt.navigation.compose)

    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.kotlinx.coroutines.guava)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.okhttp)

    // Open-source map (OpenStreetMap) and music playback.
    implementation(libs.osmdroid)
    // Optional built-in Google map; only used when the APK is built with a MAPS_API_KEY.
    implementation(libs.maps.compose)
    implementation(libs.media3.exoplayer)
    implementation(libs.media3.session)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)

    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.androidx.test.core)
    androidTestImplementation(libs.androidx.room.testing)
    androidTestImplementation(libs.kotlinx.coroutines.test)
}
