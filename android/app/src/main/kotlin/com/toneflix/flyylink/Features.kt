package com.toneflix.flyylink

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.provider.Settings
import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import com.toneflix.flyylink.protocol.PhoneFeature
import com.toneflix.flyylink.protocol.PhoneFeatures
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** The sheet explaining a sensitive permission before Android asks for it. */
enum class Disclosure { PHONE, LOCATION, FILES, CONTROL }

/** What a feature needs from Android before it can work. */
sealed interface Access {
    fun isGranted(context: Context): Boolean

    data object None : Access {
        override fun isGranted(context: Context) = true
    }

    /** Runtime permissions Android asks for in a dialog. */
    class Runtime(val permissions: () -> Array<String>) : Access {
        override fun isGranted(context: Context) =
            permissions().all { context.checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED }
    }

    /** Access the user turns on in a system settings screen. */
    class Special(private val granted: (Context) -> Boolean, val open: (Context) -> Unit) : Access {
        override fun isGranted(context: Context) = granted(context)
    }

    /** Location while in use, then "Allow all the time", which Android asks for separately. */
    data object Location : Access {
        override fun isGranted(context: Context) =
            LocationPlugin.hasPermission(context) && LocationPlugin.hasBackgroundPermission(context)
    }

    /** All files access on Android 11 and later, the storage permissions before. */
    data object Files : Access {
        override fun isGranted(context: Context) = FilesPlugin.hasAccess(context)
    }
}

/**
 * Each switch on the Features screen. Those with a [phone] feature are told to computers;
 * the others only help this phone stay connected.
 */
enum class Feature(
    val phone: PhoneFeature?,
    @DrawableRes val icon: Int,
    @StringRes val title: Int,
    @StringRes val hint: Int,
    val access: Access,
    val disclosure: Disclosure? = null,
) {
    NOTIFICATIONS(PhoneFeature.NOTIFICATIONS, R.drawable.lucide_bell, R.string.feature_notifications, R.string.feature_notifications_hint, notificationAccess()),
    MESSAGES(
        PhoneFeature.MESSAGES, R.drawable.lucide_message_square, R.string.feature_messages, R.string.feature_messages_hint,
        Access.Runtime { CallPlugin.permissions }, Disclosure.PHONE,
    ),
    CLIPBOARD(PhoneFeature.CLIPBOARD, R.drawable.lucide_clipboard, R.string.feature_clipboard, R.string.feature_clipboard_hint, Access.None),
    MEDIA(PhoneFeature.MEDIA, R.drawable.lucide_music, R.string.feature_media, R.string.feature_media_hint, notificationAccess()),
    PHOTOS(PhoneFeature.PHOTOS, R.drawable.lucide_image, R.string.feature_photos, R.string.feature_photos_hint, Access.Runtime { PhotosPlugin.permissions }),
    FILES(PhoneFeature.FILES, R.drawable.lucide_folder_open, R.string.feature_files, R.string.feature_files_hint, Access.Files, Disclosure.FILES),
    MIRRORING(PhoneFeature.MIRRORING, R.drawable.lucide_cast, R.string.feature_mirroring, R.string.feature_mirroring_hint, Access.None),
    CONTROL(
        PhoneFeature.CONTROL, R.drawable.lucide_pointer, R.string.feature_control, R.string.feature_control_hint,
        Access.Special({ ScreenControlService.status(it) == ScreenControlService.Status.ON }, { ScreenControlService.openSettings(it) }),
        Disclosure.CONTROL,
    ),
    LOCATION(PhoneFeature.LOCATION, R.drawable.lucide_map_pin, R.string.feature_location, R.string.feature_location_hint, Access.Location, Disclosure.LOCATION),
    BACKGROUND(
        null, R.drawable.lucide_battery_charging, R.string.feature_background, R.string.feature_background_hint,
        Access.Special({ LinkService.isUnrestricted(it) }) { it.startActivity(LinkService.unrestrictIntent(it).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) },
    ),
    NEARBY(null, R.drawable.lucide_bluetooth, R.string.feature_nearby, R.string.feature_nearby_hint, Access.Runtime { NearbyBeacon.permissions });

    /** A connection helper is on once Android allows it; there is nothing to turn off but the permission. */
    val isConnection: Boolean get() = phone == null

    companion object {
        val phoneFeatures: List<Feature> get() = entries.filter { !it.isConnection }

        val connection: List<Feature> get() = entries.filter { it.isConnection }

        fun of(feature: PhoneFeature): Feature = entries.first { it.phone == feature }
    }
}

private fun notificationAccess() = Access.Special({ NotificationPlugin.hasAccess(it) }) { it.startActivity(NotificationPlugin.accessSettingsIntent()) }

/**
 * Which features the user turned on. A feature works, and computers hear about it, only while
 * it is on and Android still allows what it needs. Changes go to every paired computer at once.
 */
object Features {
    private const val PREFS = "flyylink_features"
    private const val KEY_CHOSEN = "chosen"
    private const val KEY_MIGRATED = "migrated"
    private const val TRUST_PREFS = "flyylink_trusted"

    private var loaded = false

    private val _current = MutableStateFlow(PhoneFeatures.of())

    /** What this phone does now, as told to computers. */
    val current: StateFlow<PhoneFeatures> = _current.asStateFlow()

    private val _choices = MutableStateFlow<Set<Feature>>(emptySet())

    /** The switches the user turned on, whether or not Android allows them yet. */
    val choices: StateFlow<Set<Feature>> = _choices.asStateFlow()

    /** True until the user has seen the Features screen once. Earlier installs skip it. */
    fun isFirstRun(context: Context): Boolean {
        load(context)

        return !prefs(context).getBoolean(KEY_CHOSEN, false)
    }

    fun finishFirstRun(context: Context) {
        prefs(context).edit().putBoolean(KEY_CHOSEN, true).apply()
    }

    fun isOn(feature: PhoneFeature): Boolean = feature in _current.value

    @Synchronized fun set(context: Context, feature: Feature, on: Boolean) {
        if (feature.isConnection) return

        load(context)
        prefs(context).edit().putBoolean(feature.name, on).apply()
        _choices.value = if (on) _choices.value + feature else _choices.value - feature
        refresh(context)
    }

    /** Checks Android's permissions again and tells paired computers if what works has changed. */
    @Synchronized fun refresh(context: Context) {
        load(context)

        val features = working(context)
        if (features == _current.value) return

        apply(context, features)
        DeviceStore.sendToPaired(features.toPacket())
    }

    /** Reads the switches, or on the first launch of this version, keeps on what an earlier install already allowed. */
    @Synchronized private fun load(context: Context) {
        if (loaded) return

        val prefs = prefs(context)
        if (!prefs.getBoolean(KEY_MIGRATED, false)) migrate(context)

        _choices.value = Feature.phoneFeatures.filterTo(mutableSetOf()) { prefs.getBoolean(it.name, false) }
        loaded = true
        apply(context, working(context))
    }

    private fun working(context: Context): PhoneFeatures =
        PhoneFeatures(_choices.value.filter { it.access.isGranted(context) }.mapNotNullTo(mutableSetOf()) { it.phone })

    private fun apply(context: Context, features: PhoneFeatures) {
        _current.value = features
        showCopyToComputer(context, PhoneFeature.CLIPBOARD in features)
    }

    private fun migrate(context: Context) {
        val upgraded = context.getSharedPreferences(TRUST_PREFS, Context.MODE_PRIVATE).all.isNotEmpty()
        val edit = prefs(context).edit()

        Feature.phoneFeatures.forEach { feature ->
            val keep = if (feature.access == Access.None) upgraded else feature.access.isGranted(context)
            edit.putBoolean(feature.name, keep)
        }
        if (upgraded) edit.putBoolean(KEY_CHOSEN, true)

        edit.putBoolean(KEY_MIGRATED, true).apply()
    }

    /** "Copy to computer" shows over selected text only while the clipboard feature is on. */
    private fun showCopyToComputer(context: Context, show: Boolean) {
        val state = if (show) PackageManager.COMPONENT_ENABLED_STATE_DEFAULT else PackageManager.COMPONENT_ENABLED_STATE_DISABLED
        val component = ComponentName(context, CopyToComputerActivity::class.java)

        runCatching { context.packageManager.setComponentEnabledSetting(component, state, PackageManager.DONT_KILL_APP) }
    }

    /** The app's page in system settings, where the user takes back a permission. */
    fun appSettingsIntent(context: Context): Intent =
        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", context.packageName, null))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
