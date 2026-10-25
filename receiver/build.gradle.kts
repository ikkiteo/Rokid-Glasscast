plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "dev.glasscast.receiver"
    compileSdk = 34

    defaultConfig {
        applicationId = "dev.glasscast.receiver"
        minSdk = 28
        targetSdk = 32
        versionCode = 5
        versionName = "2.1.0"
    }

    flavorDimensions += "lang"
    productFlavors {
        create("ru") {
            dimension = "lang"
            isDefault = true
            buildConfigField("boolean", "EN", "false")
        }
        create("en") {
            dimension = "lang"
            buildConfigField("boolean", "EN", "true")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures {
        viewBinding = true
        buildConfig = true
    }
}

dependencies {
    implementation(project(":proto"))
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("androidx.activity:activity-ktx:1.9.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.4")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
}
