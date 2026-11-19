package com.toneflix.flyylink

import android.app.Notification
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.drawable.Icon
import android.media.MediaMetadata
import android.media.session.MediaSession
import android.media.session.PlaybackState
import android.os.SystemClock
import com.toneflix.flyylink.protocol.MediaAction
import com.toneflix.flyylink.protocol.MediaPlayer
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch
import java.io.File

/**
 * Puts what a paired computer is playing, browser tabs included, in the phone's own media controls, with play,
 * pause, skip, seek and artwork.
 */
object ComputerMedia {
    const val CHANNEL_ID = "computer_media"

    private const val TAG = "computer-media"
    private const val NOTIFICATION_ID = 1
    private const val ACTION_BUTTON = "com.toneflix.flyylink.COMPUTER_MEDIA"
    private const val EXTRA_ACTION = "action"

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var context: Context? = null
    private var session: MediaSession? = null

    /** The computer and player on show, and what the notification last said, so it's only posted again on a change. */
    private var shownDevice: String? = null
    private var posted: List<Any?>? = null

    fun start(context: Context) {
        if (this.context != null) return

        val app = context.applicationContext
        this.context = app
        scope.launch {
            combine(RemoteMediaPlugin.playing, RemoteMediaPlugin.artwork) { players, art -> players to art }
                .collect { (players, art) -> show(app, players, art) }
        }
    }

    private fun show(context: Context, players: Map<String, MediaPlayer>, art: Map<String, File>) {
        val (deviceId, player) = pick(players) ?: return hide(context)
        val device = DeviceStore.find(deviceId) ?: return hide(context)
        val artwork = player.albumArtUrl?.let(art::get)?.let { BitmapFactory.decodeFile(it.path) }
        val session = session(context)

        shownDevice = deviceId
        session.setMetadata(metadata(player, device.name, artwork))
        session.setPlaybackState(state(player))
        session.isActive = true

        val key = listOf(deviceId, player.name, player.title, player.artist, player.isPlaying, player.canGoNext, player.canGoPrevious, artwork != null)
        if (key == posted) return

        posted = key
        runCatching { manager(context).notify(TAG, NOTIFICATION_ID, notification(context, session, player, device.name, artwork)) }
    }

    /** Keeps to the computer on show while it still has the player, else the first one playing. */
    private fun pick(players: Map<String, MediaPlayer>): Pair<String, MediaPlayer>? {
        val computers = players.filterKeys { DeviceStore.find(it)?.link?.identity?.deviceType?.isComputer == true }
        val kept = shownDevice?.let { id -> computers[id]?.let { id to it } }
        if (kept?.second?.isPlaying == true) return kept

        return computers.entries.firstOrNull { it.value.isPlaying }?.toPair() ?: kept
    }

    private fun hide(context: Context) {
        if (shownDevice == null) return

        shownDevice = null
        posted = null
        manager(context).cancel(TAG, NOTIFICATION_ID)
        session?.isActive = false
    }

    private fun session(context: Context): MediaSession = session ?: MediaSession(context, TAG).also { created ->
        created.setCallback(object : MediaSession.Callback() {
            override fun onPlay() = press(MediaAction.PLAY)
            override fun onPause() = press(MediaAction.PAUSE)
            override fun onSkipToNext() = press(MediaAction.NEXT)
            override fun onSkipToPrevious() = press(MediaAction.PREVIOUS)
            override fun onStop() = press(MediaAction.STOP)
            override fun onSeekTo(position: Long) {
                shownDevice?.let { RemoteMediaPlugin.seek(it, position) }
            }
        })
        session = created
    }

    private fun press(action: MediaAction) {
        shownDevice?.let { RemoteMediaPlugin.press(it, action) }
    }

    private fun metadata(player: MediaPlayer, computer: String, artwork: Bitmap?): MediaMetadata =
        MediaMetadata.Builder()
            .putString(MediaMetadata.METADATA_KEY_TITLE, player.title.ifBlank { player.name })
            .putString(MediaMetadata.METADATA_KEY_ARTIST, player.artist.ifBlank { computer })
            .putString(MediaMetadata.METADATA_KEY_ALBUM, player.album)
            .putLong(MediaMetadata.METADATA_KEY_DURATION, player.length.takeIf { it > 0 } ?: -1)
            .apply { artwork?.let { putBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART, it) } }
            .build()

    private fun state(player: MediaPlayer): PlaybackState {
        var actions = PlaybackState.ACTION_PLAY or PlaybackState.ACTION_PAUSE or PlaybackState.ACTION_PLAY_PAUSE or PlaybackState.ACTION_STOP
        if (player.canGoNext) actions = actions or PlaybackState.ACTION_SKIP_TO_NEXT
        if (player.canGoPrevious) actions = actions or PlaybackState.ACTION_SKIP_TO_PREVIOUS
        if (player.canSeek) actions = actions or PlaybackState.ACTION_SEEK_TO

        val status = if (player.isPlaying) PlaybackState.STATE_PLAYING else PlaybackState.STATE_PAUSED

        return PlaybackState.Builder()
            .setActions(actions)
            .setState(status, player.position, if (player.isPlaying) 1f else 0f, SystemClock.elapsedRealtime())
            .build()
    }

    private fun notification(context: Context, session: MediaSession, player: MediaPlayer, computer: String, artwork: Bitmap?): Notification {
        val buttons = buildList {
            if (player.canGoPrevious) add(button(context, MediaAction.PREVIOUS, R.drawable.lucide_skip_back, R.string.media_previous))
            if (player.isPlaying) add(button(context, MediaAction.PAUSE, R.drawable.lucide_pause, R.string.media_pause))
            else add(button(context, MediaAction.PLAY, R.drawable.lucide_play, R.string.media_play))
            if (player.canGoNext) add(button(context, MediaAction.NEXT, R.drawable.lucide_skip_forward, R.string.media_next))
        }
        val compact = buttons.indices.toList().toIntArray()

        return Notification.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.lucide_laptop)
            .setContentTitle(player.title.ifBlank { player.name })
            .setContentText(player.artist.ifBlank { player.name })
            .setSubText(computer)
            .setLargeIcon(artwork?.let(Icon::createWithBitmap))
            .setStyle(Notification.MediaStyle().setMediaSession(session.sessionToken).setShowActionsInCompactView(*compact))
            .setVisibility(Notification.VISIBILITY_PUBLIC)
            .setContentIntent(MainActivity.pendingIntent(context))
            .setOngoing(player.isPlaying)
            .setShowWhen(false)
            .apply { buttons.forEach(::addAction) }
            .build()
    }

    private fun button(context: Context, action: MediaAction, icon: Int, label: Int): Notification.Action {
        val intent = Intent(context, ButtonReceiver::class.java).setAction(ACTION_BUTTON).putExtra(EXTRA_ACTION, action.wire)
        val pending = PendingIntent.getBroadcast(context, action.ordinal, intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)

        return Notification.Action.Builder(Icon.createWithResource(context, icon), context.getString(label), pending).build()
    }

    private fun manager(context: Context) = context.getSystemService(NotificationManager::class.java)

    /** The notification's buttons, on Android versions that draw them from the notification and not the session. */
    class ButtonReceiver : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            MediaAction.fromWire(intent.getStringExtra(EXTRA_ACTION))?.let(::press)
        }
    }
}
