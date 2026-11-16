package com.toneflix.flyylink

import android.Manifest
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.lifecycle.lifecycleScope
import com.toneflix.flyylink.protocol.PairState
import com.toneflix.flyylink.ui.FeaturesScreen
import com.toneflix.flyylink.ui.FlyyLinkTheme
import com.toneflix.flyylink.ui.HomeScreen
import com.toneflix.flyylink.ui.SplashOverlay
import com.toneflix.flyylink.ui.TvHomeScreen
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        val me = DeviceStore.identity(this)
        val tv = AppForm.of(this) == AppForm.TV
        val firstRun = !tv && Features.isFirstRun(this)

        if (tv) askForNotifications() else askForNotificationsOncePaired()

        setContent {
            var features by rememberSaveable { mutableStateOf(firstRun) }
            var welcome by rememberSaveable { mutableStateOf(firstRun) }

            FlyyLinkTheme(dark = tv || isSystemInDarkTheme()) {
                Box {
                    when {
                        tv -> TvHomeScreen(tvName = me.deviceName)
                        features -> FeaturesScreen(firstRun = welcome) {
                            if (welcome) Features.finishFirstRun(this@MainActivity)
                            welcome = false
                            features = false
                        }
                        else -> HomeScreen(phoneName = me.deviceName, onOpenFeatures = { features = true })
                    }
                    SplashOverlay()
                }
            }
        }
    }

    private fun askForNotifications() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return

        if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 0)
        }
    }

    /** FlyyLink's own alerts, such as pair requests and files arriving, matter once a device is paired, so ask then. */
    private fun askForNotificationsOncePaired() {
        lifecycleScope.launch {
            DeviceStore.states.first { states -> states.any { it.pairState == PairState.PAIRED } }
            askForNotifications()
        }
    }

    /** Opening or returning to the app is enough to connect, including after Disconnect or Stop. */
    override fun onStart() {
        super.onStart()
        isVisible = true
        LinkService.start(this)
    }

    override fun onStop() {
        isVisible = false
        super.onStop()
    }

    companion object {
        /** True while the app is on screen, when Android lets it open other screens without asking. */
        @Volatile var isVisible = false
            private set

        /** On a TV, brings FlyyLink to the front so someone can answer with the remote. */
        fun bringToFront(context: Context) {
            if (AppForm.of(context) != AppForm.TV || isVisible || !TvSettings.canShowOverApps(context)) return

            val open = Intent(context, MainActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_REORDER_TO_FRONT)
            runCatching { context.startActivity(open) }
        }

        /** Brings the app to the front, or opens it, when a notification is tapped. */
        fun pendingIntent(context: Context): PendingIntent {
            val open = Intent(context, MainActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_REORDER_TO_FRONT)

            return PendingIntent.getActivity(context, 0, open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        }
    }
}
