package dev.glasscast.sender

import android.app.Application
import dev.glasscast.proto.Text

class App : Application() {
    override fun onCreate() {
        super.onCreate()
        Text.en = BuildConfig.EN
    }
}
