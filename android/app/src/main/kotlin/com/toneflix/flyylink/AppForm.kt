package com.toneflix.flyylink

import android.app.UiModeManager
import android.content.Context
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.os.Build
import android.provider.Settings
import com.toneflix.flyylink.protocol.Capabilities
import com.toneflix.flyylink.protocol.DeviceIdentity
import com.toneflix.flyylink.protocol.DeviceType
import com.toneflix.flyylink.protocol.Plugin

/** Whether the app runs on a phone or a TV, and the features each one has. */
enum class AppForm(val deviceType: DeviceType) {
    PHONE(DeviceType.PHONE),
    TV(DeviceType.TV);

    val capabilities: Set<String> get() = Capabilities.forType(deviceType)

    val plugins: List<Plugin>
        get() = when (this) {
            PHONE -> listOf(
                NotificationPlugin, ComputerNotificationPlugin, BatteryPlugin, ConnectivityPlugin, FindMyPhonePlugin, ClipboardPlugin, SharePlugin, MediaPlugin, SmsPlugin, ContactsPlugin,
                CallPlugin, CallAudioPlugin, LocationPlugin, PhotosPlugin, ScreenPlugin, FlyyDropPlugin, RemoteMediaPlugin, ComputerRemotePlugin,
            )
            TV -> listOf(TvNotificationPlugin, SharePlugin, MediaPlugin, FlyyDropPlugin, TvRemotePlugin)
        }

    /** Starts the plugins that watch the system for changes to send. */
    fun startPlugins(context: Context) {
        SharePlugin.start(context)
        FlyyDropPlugin.start(context)

        when (this) {
            PHONE -> {
                ComputerNotificationPlugin.start(context)
                BatteryPlugin.start(context)
                ConnectivityPlugin.start(context)
                FindMyPhonePlugin.start(context)
                ClipboardPlugin.start(context)
                SmsPlugin.start(context)
                ContactsPlugin.start(context)
                CallPlugin.start(context)
                CallAudioPlugin.start(context)
                LocationPlugin.start(context)
                PhotosPlugin.start(context)
                ScreenPlugin.start(context)
            }
            TV -> {
                TvNotificationPlugin.start(context)
                TvRemotePlugin.start(context)
                MediaPlugin.startWithKeys(context)
            }
        }
    }

    /** The name other devices see: the TV's own name from its settings, or the phone's model. */
    fun deviceName(context: Context): String {
        val tvName = if (this == TV) Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME) else null

        return DeviceIdentity.sanitizeName(tvName ?: Build.MODEL ?: "Android")
    }

    companion object {
        /** Some Fire OS versions report this and not Leanback. */
        private const val FIRE_TV = "amazon.hardware.fire_tv"

        @Volatile private var current: AppForm? = null

        fun of(context: Context): AppForm = current ?: detect(context).also { current = it }

        private fun detect(context: Context): AppForm {
            val mode = context.getSystemService(UiModeManager::class.java)?.currentModeType
            val features = context.packageManager
            val leanback = features.hasSystemFeature(PackageManager.FEATURE_LEANBACK)
            val fireTv = features.hasSystemFeature(FIRE_TV)

            return if (mode == Configuration.UI_MODE_TYPE_TELEVISION || leanback || fireTv) TV else PHONE
        }
    }
}
