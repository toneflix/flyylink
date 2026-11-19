package com.toneflix.flyylink

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.view.KeyEvent
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.toneflix.flyylink.protocol.TvRemote
import com.toneflix.flyylink.ui.ComputerRemoteMode
import com.toneflix.flyylink.ui.ComputerRemoteScreen
import com.toneflix.flyylink.ui.FlyyLinkTheme
import com.toneflix.flyylink.ui.FullScreenTouchpad
import com.toneflix.flyylink.ui.RemoteScreen

/**
 * The phone as a remote for a paired TV or computer. The phone's volume buttons turn the device's volume while it is
 * open, or change slides on a computer's Slides tab. A computer's touchpad can fill the whole screen.
 */
class RemoteActivity : ComponentActivity() {
    private lateinit var deviceId: String
    private var computer: ComputerRemote? = null
    private var mode by mutableStateOf(ComputerRemoteMode.TOUCHPAD)
    private var fullScreen by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        deviceId = intent.getStringExtra(EXTRA_DEVICE) ?: return finish()
        if (ComputerRemotePlugin.canControl(deviceId)) computer = ComputerRemote(deviceId)
        savedInstanceState?.getString(EXTRA_MODE)?.let { saved -> ComputerRemoteMode.entries.firstOrNull { it.name == saved } }?.let { mode = it }
        if (savedInstanceState?.getBoolean(EXTRA_FULL_SCREEN) == true) showFullScreen(true)

        setContent {
            FlyyLinkTheme {
                val remote = computer
                when {
                    remote != null && fullScreen -> FullScreenTouchpad(remote, onExit = { showFullScreen(false) })
                    remote != null -> ComputerRemoteScreen(
                        deviceId,
                        remote,
                        mode,
                        onModeChange = { mode = it },
                        onFullScreen = { showFullScreen(true) },
                        onClose = ::finish,
                    )
                    else -> RemoteScreen(deviceId, onPress = ::press, onClose = ::finish)
                }
            }
        }
    }

    override fun onStart() {
        super.onStart()
        computer?.begin()
    }

    override fun onStop() {
        computer?.release()
        super.onStop()
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        outState.putString(EXTRA_MODE, mode.name)
        outState.putBoolean(EXTRA_FULL_SCREEN, fullScreen)
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        val up = when (keyCode) {
            KeyEvent.KEYCODE_VOLUME_UP -> true
            KeyEvent.KEYCODE_VOLUME_DOWN -> false
            else -> return super.onKeyDown(keyCode, event)
        }

        val remote = computer
        when {
            remote == null -> press(TvRemote.Key(if (up) TvRemote.VOLUME_UP else TvRemote.VOLUME_DOWN))
            mode == ComputerRemoteMode.SLIDES -> if (up) remote.previousSlide() else remote.nextSlide()
            else -> remote.press(if (up) ComputerRemote.VOLUME_UP else ComputerRemote.VOLUME_DOWN)
        }

        return true
    }

    /** Hides the status and navigation bars and keeps the screen awake while the touchpad fills the screen. */
    private fun showFullScreen(on: Boolean) {
        val bars = WindowCompat.getInsetsController(window, window.decorView)

        fullScreen = on
        if (on) {
            bars.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            bars.hide(WindowInsetsCompat.Type.systemBars())
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            bars.show(WindowInsetsCompat.Type.systemBars())
            window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    private fun press(press: TvRemote) {
        DeviceStore.find(deviceId)?.let { DeviceStore.send(it, press.toPacket()) }
    }

    companion object {
        private const val EXTRA_DEVICE = "device"
        private const val EXTRA_MODE = "mode"
        private const val EXTRA_FULL_SCREEN = "fullScreen"

        fun intent(context: Context, deviceId: String): Intent =
            Intent(context, RemoteActivity::class.java).putExtra(EXTRA_DEVICE, deviceId)
    }
}
