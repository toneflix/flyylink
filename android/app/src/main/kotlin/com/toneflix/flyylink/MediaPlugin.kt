package com.toneflix.flyylink

import android.content.ComponentName
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.media.AudioManager
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.view.KeyEvent
import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.Media
import com.toneflix.flyylink.protocol.MediaAction
import com.toneflix.flyylink.protocol.MediaPlayer
import com.toneflix.flyylink.protocol.MediaRequest
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.PhoneNotification
import com.toneflix.flyylink.protocol.Plugin
import java.io.ByteArrayOutputStream

/**
 * Lets the Mac see and control what is playing on the phone or TV. Android only shares media
 * sessions with apps that have notification access, so [NotificationMirrorService] starts this.
 * A TV without that access offers one player, [KEYS_PLAYER], worked with media keys.
 */
object MediaPlugin : Plugin {
    override val incoming = setOf(PacketType.MPRIS_REQUEST)

    private const val ART_SIZE = 512
    private const val ART_URL_PREFIX = "flyylink://art/"
    const val KEYS_PLAYER = "TV"

    private class Session(val name: String, val controller: MediaController, val callback: MediaController.Callback)

    private class Art(val bitmap: Bitmap, val url: String, val bytes: ByteArray)

    private val main = Handler(Looper.getMainLooper())
    private var context: Context? = null
    private var manager: MediaSessionManager? = null

    // Touched only on the main thread.
    private var sessions = listOf<Session>()
    private val art = mutableMapOf<String, Art>()

    /** Set on a TV, so media keys stand in for the sessions until notification access is given. */
    private var audio: AudioManager? = null

    private val sessionsChanged = MediaSessionManager.OnActiveSessionsChangedListener { update(it.orEmpty()) }

    fun start(service: NotificationMirrorService) {
        main.post { listen(service) }
    }

    fun stop() {
        main.post { forget() }
    }

    fun startWithKeys(context: Context) {
        audio = context.applicationContext.getSystemService(AudioManager::class.java)
    }

    private val usesKeys: Boolean get() = manager == null && audio != null

    private fun listen(service: NotificationMirrorService) {
        if (manager != null) return

        val manager = service.getSystemService(MediaSessionManager::class.java)
        val component = ComponentName(service, NotificationMirrorService::class.java)
        context = service.applicationContext
        this.manager = manager

        runCatching {
            manager.addOnActiveSessionsChangedListener(sessionsChanged, component, main)
            update(manager.getActiveSessions(component))
        }
    }

    private fun forget() {
        runCatching { manager?.removeOnActiveSessionsChangedListener(sessionsChanged) }
        sessions.forEach { it.controller.unregisterCallback(it.callback) }
        sessions = emptyList()
        art.clear()
        manager = null
    }

    override fun onPaired(device: Device) {
        main.post { announce(device) }
    }

    override fun onPacket(device: Device, packet: NetworkPacket) {
        val request = MediaRequest.fromPacket(packet) ?: return

        main.post { handle(device, request) }
    }

    private fun handle(device: Device, request: MediaRequest) {
        if (request.wantsPlayerList) announce(device)

        if (usesKeys && request.player == KEYS_PLAYER) {
            pressKey(device, request)
            return
        }

        val session = sessions.firstOrNull { it.name == request.player } ?: return
        val controls = session.controller.transportControls

        when (request.action) {
            MediaAction.PLAY_PAUSE -> if (session.controller.isPlaying()) controls.pause() else controls.play()
            MediaAction.PLAY -> controls.play()
            MediaAction.PAUSE -> controls.pause()
            MediaAction.NEXT -> controls.skipToNext()
            MediaAction.PREVIOUS -> controls.skipToPrevious()
            MediaAction.STOP -> controls.stop()
            null -> Unit
        }

        request.seekTo?.let { controls.seekTo(it) }
        request.volume?.let { volume ->
            val max = session.controller.playbackInfo?.maxVolume ?: return@let
            session.controller.setVolumeTo(volume * max / 100, 0)
        }

        if (request.wantsNowPlaying) DeviceStore.send(device, describe(session).toPacket())
        request.albumArtUrl?.let { url -> sendArt(device, session.name, url) }
    }

    /** Keeps one callback per active session, and tells the Macs when the set changes. */
    private fun update(controllers: List<MediaController>) {
        sessions.forEach { it.controller.unregisterCallback(it.callback) }

        // FlyyLink's own session shows the computer's media, which the computer already has.
        val own = context?.packageName
        val names = mutableSetOf<String>()
        sessions = controllers.filter { it.packageName != own }.map { controller ->
            val base = appName(controller.packageName)
            var name = base
            var copy = 1
            while (!names.add(name)) name = "$base ${++copy}"

            val callback = object : MediaController.Callback() {
                override fun onPlaybackStateChanged(state: PlaybackState?) = changed(name)
                override fun onMetadataChanged(metadata: MediaMetadata?) = changed(name)
                override fun onAudioInfoChanged(info: MediaController.PlaybackInfo) = changed(name)
            }
            controller.registerCallback(callback, main)
            Session(name, controller, callback)
        }
        art.keys.retainAll(names)

        DeviceStore.pairedDevices().forEach { announce(it) }
    }

    private fun pressKey(device: Device, request: MediaRequest) {
        val audio = audio ?: return
        val code = when (request.action) {
            MediaAction.PLAY_PAUSE -> KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE
            MediaAction.PLAY -> KeyEvent.KEYCODE_MEDIA_PLAY
            MediaAction.PAUSE -> KeyEvent.KEYCODE_MEDIA_PAUSE
            MediaAction.NEXT -> KeyEvent.KEYCODE_MEDIA_NEXT
            MediaAction.PREVIOUS -> KeyEvent.KEYCODE_MEDIA_PREVIOUS
            MediaAction.STOP -> KeyEvent.KEYCODE_MEDIA_STOP
            null -> null
        }

        code?.let { TvRemotePlugin.media(audio, it) }
        if (request.wantsNowPlaying || code != null) DeviceStore.send(device, keysPlayer(audio).toPacket())
    }

    private fun keysPlayer(audio: AudioManager) = MediaPlayer(
        name = KEYS_PLAYER,
        isPlaying = audio.isMusicActive,
        canGoNext = true,
        canGoPrevious = true,
    )

    private fun announce(device: Device) {
        val audio = audio
        if (usesKeys && audio != null) {
            DeviceStore.send(device, Media.playerListPacket(listOf(KEYS_PLAYER)))
            DeviceStore.send(device, keysPlayer(audio).toPacket())
            return
        }

        DeviceStore.send(device, Media.playerListPacket(sessions.map { it.name }))
        sessions.forEach { DeviceStore.send(device, describe(it).toPacket()) }
    }

    private fun changed(name: String) {
        val session = sessions.firstOrNull { it.name == name } ?: return

        DeviceStore.sendToPaired(describe(session).toPacket())
    }

    private fun describe(session: Session): MediaPlayer {
        val controller = session.controller
        val metadata = controller.metadata
        val state = controller.playbackState
        val actions = state?.actions ?: 0L
        val volume = controller.playbackInfo?.let { if (it.maxVolume > 0) it.currentVolume * 100 / it.maxVolume else null }

        return MediaPlayer(
            name = session.name,
            title = metadata?.getString(MediaMetadata.METADATA_KEY_TITLE).orEmpty(),
            artist = (metadata?.getString(MediaMetadata.METADATA_KEY_ARTIST) ?: metadata?.getString(MediaMetadata.METADATA_KEY_ALBUM_ARTIST)).orEmpty(),
            album = metadata?.getString(MediaMetadata.METADATA_KEY_ALBUM).orEmpty(),
            isPlaying = controller.isPlaying(),
            position = state?.currentPosition() ?: 0,
            length = metadata?.getLong(MediaMetadata.METADATA_KEY_DURATION)?.coerceAtLeast(0) ?: 0,
            volume = volume,
            canPlay = actions has PlaybackState.ACTION_PLAY || actions has PlaybackState.ACTION_PLAY_PAUSE,
            canPause = actions has PlaybackState.ACTION_PAUSE || actions has PlaybackState.ACTION_PLAY_PAUSE,
            canGoNext = actions has PlaybackState.ACTION_SKIP_TO_NEXT,
            canGoPrevious = actions has PlaybackState.ACTION_SKIP_TO_PREVIOUS,
            canSeek = actions has PlaybackState.ACTION_SEEK_TO,
            albumArtUrl = metadata?.let { artFor(session.name, it) }?.url,
        )
    }

    /** The cover as a small JPEG, named by its hash so the Mac only fetches a new one. */
    private fun artFor(name: String, metadata: MediaMetadata): Art? {
        val bitmap = metadata.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART)
            ?: metadata.getBitmap(MediaMetadata.METADATA_KEY_ART)
            ?: metadata.getBitmap(MediaMetadata.METADATA_KEY_DISPLAY_ICON)
            ?: return null
        art[name]?.takeIf { it.bitmap === bitmap }?.let { return it }

        val scale = minOf(1f, ART_SIZE.toFloat() / maxOf(bitmap.width, bitmap.height))
        val scaled = if (scale < 1f) Bitmap.createScaledBitmap(bitmap, (bitmap.width * scale).toInt(), (bitmap.height * scale).toInt(), true) else bitmap
        val bytes = ByteArrayOutputStream().also { scaled.compress(Bitmap.CompressFormat.JPEG, 88, it) }.toByteArray()

        return Art(bitmap, ART_URL_PREFIX + PhoneNotification.iconHash(bytes), bytes).also { art[name] = it }
    }

    private fun sendArt(device: Device, player: String, url: String) {
        val cover = art[player]?.takeIf { it.url == url } ?: return

        DeviceStore.sendWithPayload(device, Media.albumArtPacket(player, url), cover.bytes)
    }

    private fun MediaController.isPlaying(): Boolean = playbackState?.state == PlaybackState.STATE_PLAYING

    /** Where playback is now, since the state only records where it was at its last update. */
    private fun PlaybackState.currentPosition(): Long {
        if (state != PlaybackState.STATE_PLAYING || lastPositionUpdateTime <= 0) return position

        val elapsed = SystemClock.elapsedRealtime() - lastPositionUpdateTime
        return (position + elapsed * playbackSpeed).toLong().coerceAtLeast(0)
    }

    private infix fun Long.has(action: Long): Boolean = this and action != 0L

    private fun appName(packageName: String): String {
        val pm = context?.packageManager ?: return packageName

        return try {
            pm.getApplicationLabel(pm.getApplicationInfo(packageName, 0)).toString()
        } catch (_: PackageManager.NameNotFoundException) {
            packageName
        }
    }
}
