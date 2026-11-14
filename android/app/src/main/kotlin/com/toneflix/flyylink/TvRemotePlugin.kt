package com.toneflix.flyylink

import android.content.Context
import android.media.AudioManager
import android.os.SystemClock
import android.view.KeyEvent
import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.Plugin
import com.toneflix.flyylink.protocol.TvRemote

/**
 * Plays a phone's remote presses on the TV. Volume and media keys work on their own; moving around,
 * Back, Home, typing and the touchpad's pointer go through [ScreenControlService], which the user turns on once.
 */
object TvRemotePlugin : Plugin {
    override val incoming = setOf(PacketType.REMOTE)

    private var audio: AudioManager? = null

    fun start(context: Context) {
        audio = context.applicationContext.getSystemService(AudioManager::class.java)
    }

    override fun onPacket(device: Device, packet: NetworkPacket) {
        val press = TvRemote.fromPacket(packet) ?: return

        if (press is TvRemote.Key) key(press.key) else ScreenControlService.remote(press)
    }

    private fun key(key: String) {
        val audio = audio ?: return

        when (key) {
            TvRemote.VOLUME_UP -> audio.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_RAISE, AudioManager.FLAG_SHOW_UI)
            TvRemote.VOLUME_DOWN -> audio.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_LOWER, AudioManager.FLAG_SHOW_UI)
            TvRemote.MUTE -> audio.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_TOGGLE_MUTE, AudioManager.FLAG_SHOW_UI)
            TvRemote.PLAY_PAUSE -> media(audio, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE)
            TvRemote.NEXT -> media(audio, KeyEvent.KEYCODE_MEDIA_NEXT)
            TvRemote.PREVIOUS -> media(audio, KeyEvent.KEYCODE_MEDIA_PREVIOUS)
            else -> ScreenControlService.remote(TvRemote.Key(key))
        }
    }

    /** Presses a media key for whatever app is playing. */
    fun media(audio: AudioManager, code: Int) {
        val time = SystemClock.uptimeMillis()

        audio.dispatchMediaKeyEvent(KeyEvent(time, time, KeyEvent.ACTION_DOWN, code, 0))
        audio.dispatchMediaKeyEvent(KeyEvent(time, time, KeyEvent.ACTION_UP, code, 0))
    }
}
