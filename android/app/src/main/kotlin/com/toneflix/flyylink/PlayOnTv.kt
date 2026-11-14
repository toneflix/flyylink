package com.toneflix.flyylink

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.webkit.MimeTypeMap
import android.widget.Toast
import com.toneflix.flyylink.protocol.CastKind
import com.toneflix.flyylink.protocol.CastState
import com.toneflix.flyylink.protocol.MediaServer
import com.toneflix.flyylink.protocol.TvFinder
import com.toneflix.flyylink.protocol.TvList
import com.toneflix.flyylink.protocol.TvPlayer
import com.toneflix.flyylink.protocol.TvRenderer
import java.io.InputStream
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/** A photo, video or song that can be played on a TV. */
data class CastItem(val uri: Uri, val name: String, val size: Long, val mime: String) {
    val kind: CastKind get() = CastKind.of(mime) ?: CastKind.VIDEO

    companion object {
        /** Null unless [item] is a photo, video or song. */
        fun of(context: Context, item: ShareSender.Item): CastItem? {
            val extension = item.name.substringAfterLast('.', "").lowercase()
            val mime = context.contentResolver.getType(item.uri)?.takeIf { CastKind.of(it) != null }
                ?: MimeTypeMap.getSingleton().getMimeTypeFromExtension(extension)
                ?: return null

            return if (CastKind.of(mime) == null) null else CastItem(item.uri, item.name, item.size, mime)
        }
    }
}

/** What is playing on a TV right now. Times are in milliseconds; a duration of 0 means unknown. */
data class TvSession(
    val tv: TvRenderer,
    val item: CastItem,
    val state: CastState = CastState.LOADING,
    val position: Long = 0,
    val duration: Long = 0,
)

/** "Play on TV": finds DLNA TVs, serves the chosen file to one and controls what it plays. */
object PlayOnTv {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val main = Handler(Looper.getMainLooper())

    private val _tvs = MutableStateFlow<List<TvRenderer>>(emptyList())
    private val _searching = MutableStateFlow(false)
    private val _session = MutableStateFlow<TvSession?>(null)

    val tvs: StateFlow<List<TvRenderer>> = _tvs.asStateFlow()
    val searching: StateFlow<Boolean> = _searching.asStateFlow()
    val session: StateFlow<TvSession?> = _session.asStateFlow()

    private val seen = TvList()
    private var server: MediaServer? = null
    private var player: TvPlayer? = null
    private var watcher: Job? = null

    fun search() {
        if (!_searching.compareAndSet(expect = false, update = true)) return

        scope.launch {
            try {
                _tvs.value = seen.update(TvFinder.search())
            } finally {
                _searching.value = false
            }
        }
    }

    /** Searches again every [intervalMs] until cancelled, so the list follows TVs as they come and go. */
    suspend fun keepSearching(intervalMs: Long = 30_000) {
        while (true) {
            search()
            delay(intervalMs)
        }
    }

    fun play(context: Context, item: CastItem, tv: TvRenderer) {
        val app = context.applicationContext

        // TVs read the file many times, so keep access after the screen that picked it closes.
        runCatching { app.contentResolver.takePersistableUriPermission(item.uri, Intent.FLAG_GRANT_READ_URI_PERMISSION) }

        scope.launch {
            watcher?.cancel()
            player?.let { runCatching { it.stop() } }

            val tvPlayer = TvPlayer(tv)
            player = tvPlayer
            _session.value = TvSession(tv, item)

            val played = runCatching {
                val media = mediaServer().apply { clear() }
                val url = media.share(UriSource(app, item), tv.host)

                tvPlayer.play(url, item.name, item.mime, item.size)
            }

            if (played.isSuccess) {
                toast(app, app.getString(R.string.play_on_tv_playing, tv.name))
                watch(tvPlayer)
            } else {
                end()
                toast(app, app.getString(R.string.play_on_tv_failed, tv.name))
            }
        }
    }

    fun togglePause() {
        val current = _session.value ?: return
        val tvPlayer = player ?: return
        val paused = current.state == CastState.PAUSED

        _session.value = current.copy(state = if (paused) CastState.PLAYING else CastState.PAUSED)
        scope.launch { runCatching { if (paused) tvPlayer.resume() else tvPlayer.pause() } }
    }

    fun seek(positionMs: Long) {
        val tvPlayer = player ?: return

        _session.update { it?.copy(position = positionMs) }
        scope.launch { runCatching { tvPlayer.seek(positionMs) } }
    }

    fun stop() {
        val tvPlayer = player

        end()
        scope.launch { tvPlayer?.let { runCatching { it.stop() } } }
    }

    /** Follows the TV about once a second, and ends the session once it stops on its own. */
    private fun watch(tvPlayer: TvPlayer) {
        watcher = scope.launch {
            var started = false

            while (isActive && player === tvPlayer) {
                val state = runCatching { tvPlayer.state() }.getOrNull()
                val position = runCatching { tvPlayer.position() }.getOrNull()

                if (state == CastState.PLAYING || state == CastState.PAUSED) started = true

                if (started && state == CastState.STOPPED && _session.value?.item?.kind != CastKind.PHOTO) {
                    end()
                    break
                }

                _session.update { current ->
                    current?.copy(
                        state = state ?: current.state,
                        position = position?.position ?: current.position,
                        duration = position?.duration?.takeIf { it > 0 } ?: current.duration,
                    )
                }

                delay(1000)
            }
        }
    }

    private fun end() {
        watcher?.cancel()
        watcher = null
        player = null
        server?.clear()
        _session.value = null
    }

    @Synchronized
    private fun mediaServer(): MediaServer = server ?: MediaServer().also { server = it }

    private fun toast(context: Context, text: String) {
        main.post { Toast.makeText(context, text, Toast.LENGTH_SHORT).show() }
    }

    private class UriSource(private val context: Context, private val item: CastItem) : MediaServer.Source {
        override val name: String get() = item.name
        override val mime: String get() = item.mime
        override val size: Long get() = item.size

        override fun open(): InputStream = context.contentResolver.openInputStream(item.uri) ?: throw java.io.FileNotFoundException(item.uri.toString())
    }
}
