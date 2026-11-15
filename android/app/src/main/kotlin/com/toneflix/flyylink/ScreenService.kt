package com.toneflix.flyylink

import android.Manifest
import android.annotation.SuppressLint
import android.app.Notification
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.content.res.Configuration
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaCodecList
import android.media.MediaFormat
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.Surface
import android.view.WindowManager
import androidx.annotation.RequiresApi
import com.toneflix.flyylink.protocol.Screen
import com.toneflix.flyylink.protocol.ScreenFrame
import java.io.InputStream
import java.util.concurrent.LinkedBlockingQueue
import kotlin.concurrent.thread
import kotlin.math.roundToInt

/**
 * Records the screen into H.264 and streams it to the Mac over a payload connection.
 * The encoder restarts at the new size when the phone rotates; the stream carries the new settings.
 * When the Mac asks for sound and Android allows it, the phone's playback goes into the same stream as PCM.
 */
class ScreenService : Service() {
    private var projection: MediaProjection? = null
    private var display: VirtualDisplay? = null
    private var encoder: Encoder? = null
    private var sound: SoundCapture? = null
    private var frames: FrameQueue? = null
    private var deviceId: String? = null
    private var request: ScreenPlugin.Pending? = null
    private var orientation = Configuration.ORIENTATION_UNDEFINED
    private val main = Handler(Looper.getMainLooper())
    private val abilities by lazy { EncoderAbilities.find() }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent == null) return START_NOT_STICKY

        if (intent.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }

        val resultData = intent.extraData()
        if (projection != null || resultData == null) return START_NOT_STICKY

        // A service started with startForegroundService must go foreground, even when it then gives up.
        val pending = ScreenPlugin.pending
        val device = pending?.let { DeviceStore.find(it.deviceId) }
        val withSound = pending?.audio == true && canRecordSound()
        goForeground(device?.name ?: "", withSound)

        if (pending == null || device == null || !device.isPaired) {
            stopSelf()
            return START_NOT_STICKY
        }

        val projection = runCatching {
            getSystemService(MediaProjectionManager::class.java)
                .getMediaProjection(intent.getIntExtra(EXTRA_RESULT, 0), resultData)
        }.onFailure { Log.w(TAG, "Could not start the recording", it) }.getOrNull()
        if (projection == null) {
            stopSelf()
            return START_NOT_STICKY
        }

        projection.registerCallback(object : MediaProjection.Callback() {
            override fun onStop() {
                main.post { stopSelf() }
            }
        }, main)

        this.projection = projection
        this.deviceId = device.id
        this.request = pending
        orientation = resources.configuration.orientation
        isRunning = true

        val queue = FrameQueue()
        frames = queue
        val (width, height) = videoSize(pending.maxSize)

        // Encoding starts once the Mac connects, so the first frame it gets is the settings and a key frame.
        DeviceStore.offer(device, Screen.started(width, height, audio = withSound), Screen.STREAM, open = {
            main.post {
                startEncoding(width, height)
                if (withSound) startSound()
            }
            queue.stream
        }) { error ->
            Log.i(TAG, "Screen stream ended: ${error?.message ?: "closed"}")
            main.post { stopSelf() }
        }

        return START_NOT_STICKY
    }

    private fun goForeground(macName: String, withSound: Boolean) {
        val shown = notification(macName)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R && withSound) {
            // Recording sound counts as using the microphone; if Android refuses, mirror without it.
            val started = runCatching {
                startForeground(
                    NOTIFICATION_ID,
                    shown,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION or ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE,
                )
            }
            if (started.isSuccess) return
            Log.w(TAG, "Could not record sound in the background", started.exceptionOrNull())
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, shown, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
        } else {
            startForeground(NOTIFICATION_ID, shown)
        }
    }

    private fun canRecordSound(): Boolean =
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q &&
            checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED

    private fun startSound() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q || sound != null) return
        val projection = projection ?: return
        val queue = frames ?: return

        sound = runCatching { SoundCapture(projection, queue) }
            .onFailure { Log.w(TAG, "Could not record the phone's sound", it) }
            .getOrNull()
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)

        if (newConfig.orientation == orientation || encoder == null) return
        orientation = newConfig.orientation

        // Wait a moment so the display reports its rotated size.
        main.postDelayed({
            val request = request ?: return@postDelayed
            val (width, height) = videoSize(request.maxSize)
            startEncoding(width, height)
        }, 300)
    }

    override fun onDestroy() {
        isRunning = false
        sound?.stop()
        sound = null
        encoder?.stop()
        encoder = null
        display?.release()
        display = null
        projection?.stop()
        projection = null
        frames?.close()
        frames = null
        deviceId?.let { id -> DeviceStore.find(id)?.let { DeviceStore.send(it, Screen.stopped()) } }
        super.onDestroy()
    }

    /** Starts (or restarts, after a rotation) the encoder and points the recording at it. */
    private fun startEncoding(width: Int, height: Int) {
        val projection = projection ?: return
        val queue = frames ?: return
        val request = request ?: return

        val previous = encoder
        val start = previous?.bitrate ?: request.bitrate
        val sizes = listOf(width to height, abilities.fit(width, height, SAFE_SIZE)).distinct()
        val next = sizes.firstNotNullOfOrNull { (w, h) ->
            runCatching { Encoder(abilities, w, h, request.bitrate, start, queue) }
                .onFailure { Log.w(TAG, "Could not start the encoder at ${w}x$h", it) }
                .getOrNull()
        }
        if (next == null) {
            stopSelf()
            return
        }
        encoder = next

        val density = resources.displayMetrics.densityDpi
        val current = display
        if (current == null) {
            display = runCatching {
                projection.createVirtualDisplay(
                    "FlyyLink",
                    next.width,
                    next.height,
                    density,
                    DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                    next.surface,
                    null,
                    null,
                )
            }.getOrElse {
                Log.w(TAG, "Could not start the recording", it)
                stopSelf()
                return
            }
        } else {
            current.resize(next.width, next.height, density)
            current.surface = next.surface
        }

        // Only now that the recording points at the new encoder can the old one go.
        previous?.stop()
    }

    /** The screen's size, shrunk only as far as [maxSize] and the phone's encoder need. */
    private fun videoSize(maxSize: Int): Pair<Int, Int> {
        val windows = getSystemService(WindowManager::class.java)
        val (realWidth, realHeight) = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            windows.maximumWindowMetrics.bounds.let { it.width() to it.height() }
        } else {
            val metrics = android.util.DisplayMetrics()
            @Suppress("DEPRECATION")
            windows.defaultDisplay.getRealMetrics(metrics)
            metrics.widthPixels to metrics.heightPixels
        }

        return abilities.fit(realWidth, realHeight, maxSize)
    }

    private fun notification(macName: String): Notification {
        val stop = PendingIntent.getService(
            this, 0, Intent(this, ScreenService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE,
        )

        return Notification.Builder(this, ScreenPlugin.CHANNEL_ID)
            .setSmallIcon(R.drawable.lucide_cast)
            .setContentTitle(getString(R.string.screen_mirroring, macName))
            .setContentIntent(MainActivity.pendingIntent(this))
            .setOngoing(true)
            .addAction(Notification.Action.Builder(null, getString(R.string.screen_stop), stop).build())
            .build()
    }

    private fun Intent.extraData(): Intent? =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            getParcelableExtra(EXTRA_DATA, Intent::class.java)
        } else {
            @Suppress("DEPRECATION")
            getParcelableExtra(EXTRA_DATA)
        }

    /**
     * The phone's H.264 encoder and what it can do, so the stream uses the sharpest size, best profile and
     * rate control it supports.
     */
    private class EncoderAbilities(private val name: String?, private val capabilities: MediaCodecInfo.CodecCapabilities?) {
        private val video: MediaCodecInfo.VideoCapabilities? get() = capabilities?.videoCapabilities

        val variableBitrate: Boolean
            get() = capabilities?.encoderCapabilities?.isBitrateModeSupported(MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_VBR) == true

        /** High profile when the phone can encode it without B-frames, which a live stream can't wait for. */
        val profile: Pair<Int, Int>?
            get() {
                val levels = capabilities?.profileLevels ?: return null
                val wanted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    listOf(MediaCodecInfo.CodecProfileLevel.AVCProfileHigh, MediaCodecInfo.CodecProfileLevel.AVCProfileMain, MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline)
                } else {
                    listOf(MediaCodecInfo.CodecProfileLevel.AVCProfileBaseline)
                }

                return wanted.firstNotNullOfOrNull { profile ->
                    levels.filter { it.profile == profile }.maxOfOrNull { it.level }?.let { profile to it }
                }
            }

        fun create(): MediaCodec =
            name?.let { runCatching { MediaCodec.createByCodecName(it) }.getOrNull() }
                ?: MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)

        fun bitrate(requested: Int): Int = video?.bitrateRange?.clamp(requested) ?: requested

        /** [width] by [height] shrunk so the long side is at most [maxSize] and the encoder can take it at full frame rate. */
        fun fit(width: Int, height: Int, maxSize: Int): Pair<Int, Int> {
            val longSide = maxOf(width, height)
            var limit = minOf(maxSize, longSide)

            while (true) {
                val scale = limit.toDouble() / longSide
                val size = align(width * scale, video?.widthAlignment) to align(height * scale, video?.heightAlignment)
                if (limit <= MIN_SIDE || supports(size)) return size

                limit -= STEP
            }
        }

        private fun supports(size: Pair<Int, Int>): Boolean {
            val video = video ?: return true

            return runCatching { video.areSizeAndRateSupported(size.first, size.second, FRAME_RATE.toDouble()) }.getOrDefault(false)
        }

        private fun align(side: Double, alignment: Int?): Int {
            val step = maxOf(ALIGNMENT, alignment ?: 1)

            return ((side / step).roundToInt() * step).coerceAtLeast(step)
        }

        companion object {
            /** Multiples of 8 keep most phone screens at their exact size, so nothing is resampled. */
            private const val ALIGNMENT = 8
            private const val MIN_SIDE = 960
            private const val STEP = 160

            /** The first hardware H.264 encoder, or any H.264 encoder when there is none. */
            fun find(): EncoderAbilities {
                val encoders = runCatching {
                    MediaCodecList(MediaCodecList.REGULAR_CODECS).codecInfos.filter { info ->
                        info.isEncoder && info.supportedTypes.any { it.equals(MediaFormat.MIMETYPE_VIDEO_AVC, ignoreCase = true) }
                    }
                }.getOrDefault(emptyList())
                val info = encoders.firstOrNull { it.isHardware() } ?: encoders.firstOrNull()
                val capabilities = info?.let { runCatching { it.getCapabilitiesForType(MediaFormat.MIMETYPE_VIDEO_AVC) }.getOrNull() }

                return EncoderAbilities(info?.name, capabilities)
            }

            private fun MediaCodecInfo.isHardware(): Boolean =
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    isHardwareAccelerated
                } else {
                    !name.startsWith("OMX.google.") && !name.startsWith("c2.android.")
                }
        }
    }

    /**
     * One H.264 encoder fed by a surface, with a thread that moves its output into the stream. It lowers the
     * bitrate while the Wi-Fi falls behind and raises it again once it keeps up.
     */
    private class Encoder(
        abilities: EncoderAbilities,
        val width: Int,
        val height: Int,
        target: Int,
        start: Int,
        private val queue: FrameQueue,
    ) {
        private val codec = abilities.create()
        private val control = BitrateControl(abilities.bitrate(target), abilities.bitrate(start))
        val surface: Surface
        @Volatile private var running = true
        private var skipping = false

        val bitrate: Int get() = control.current

        init {
            // Some encoders reject the tuned settings, so fall back to their defaults.
            runCatching { codec.configure(format(abilities, tuned = true), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE) }
                .onFailure {
                    codec.reset()
                    codec.configure(format(abilities, tuned = false), null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
                }
            surface = codec.createInputSurface()
            codec.start()

            thread(name = "flyylink-screen", isDaemon = true) { drain() }
        }

        private fun format(abilities: EncoderAbilities, tuned: Boolean) =
            MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height).apply {
                setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
                setInteger(MediaFormat.KEY_BIT_RATE, control.current)
                setInteger(MediaFormat.KEY_FRAME_RATE, FRAME_RATE)
                setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, KEY_FRAME_SECONDS)
                // A still screen makes no new frames; repeat the last one so the Mac never waits long.
                setLong(MediaFormat.KEY_REPEAT_PREVIOUS_FRAME_AFTER, 100_000)
                setInteger(MediaFormat.KEY_PRIORITY, 0)
                if (tuned) {
                    abilities.profile?.let { (profile, level) ->
                        setInteger(MediaFormat.KEY_PROFILE, profile)
                        setInteger(MediaFormat.KEY_LEVEL, level)
                    }
                    if (abilities.variableBitrate) setInteger(MediaFormat.KEY_BITRATE_MODE, MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_VBR)
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) setInteger(MediaFormat.KEY_MAX_B_FRAMES, 0)
                }
            }

        private fun drain() {
            val info = MediaCodec.BufferInfo()

            // The codec throws once it is stopped, which ends the loop.
            runCatching {
                while (running) {
                    val index = codec.dequeueOutputBuffer(info, 100_000)
                    if (index < 0) continue

                    val buffer = codec.getOutputBuffer(index)
                    if (buffer != null && info.size > 0) {
                        val bytes = ByteArray(info.size)
                        buffer.position(info.offset)
                        buffer.get(bytes)
                        send(bytes, info)
                    }
                    codec.releaseOutputBuffer(index, false)
                }
            }
        }

        /** Drops frames while the Wi-Fi can't keep up, then starts again cleanly from a key frame. */
        private fun send(bytes: ByteArray, info: MediaCodec.BufferInfo) {
            var flags = 0
            if (info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0) flags = flags or ScreenFrame.CONFIG
            if (info.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME != 0) flags = flags or ScreenFrame.KEY
            val frame = ScreenFrame(bytes, info.presentationTimeUs, flags)
            val backlog = queue.backlog

            control.adjust(backlog)?.let { bitrate ->
                runCatching { codec.setParameters(Bundle().apply { putInt(MediaCodec.PARAMETER_KEY_VIDEO_BITRATE, bitrate) }) }
            }

            if (!frame.isConfig && backlog > MAX_BACKLOG && !skipping) {
                skipping = true
                runCatching { codec.setParameters(Bundle().apply { putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0) }) }
            }
            if (skipping && frame.isKey) skipping = false
            if (skipping && !frame.isConfig) return

            queue.put(frame.header() + frame.data)
        }

        fun stop() {
            running = false
            runCatching { codec.stop() }
            runCatching { codec.release() }
            surface.release()
        }

        private companion object {
            const val MAX_BACKLOG = 20

            /** Key frames are only needed to recover from dropped frames, which ask for one anyway. */
            const val KEY_FRAME_SECONDS = 10
        }
    }

    /** Picks the bitrate from how much of the stream is waiting to go out, between a floor and [target]. */
    private class BitrateControl(private val target: Int, start: Int) {
        var current = start.coerceIn(minOf(FLOOR, target), target)
            private set
        private var changedAt = SystemClock.elapsedRealtime()
        private var busyAt = changedAt

        /** The bitrate to switch to, or null to keep the current one. */
        fun adjust(backlog: Int): Int? {
            val now = SystemClock.elapsedRealtime()
            if (backlog > CLEAR_BACKLOG) busyAt = now

            val next = when {
                backlog >= SLOW_BACKLOG && now - changedAt >= DOWN_WAIT_MS -> maxOf(minOf(FLOOR, target), (current * 0.7).toInt())
                current < target && now - busyAt >= UP_WAIT_MS && now - changedAt >= UP_WAIT_MS -> minOf(target, (current * 1.2).toInt())
                else -> return null
            }
            if (next == current) return null

            current = next
            changedAt = now

            return next
        }

        private companion object {
            const val FLOOR = 2_000_000
            const val CLEAR_BACKLOG = 2
            const val SLOW_BACKLOG = 8
            const val DOWN_WAIT_MS = 1_000L
            const val UP_WAIT_MS = 4_000L
        }
    }

    /**
     * Records what the phone plays (music, videos, games; not calls, and not apps that opt out) in
     * 20 ms pieces of 16-bit stereo PCM, sent as [ScreenFrame.AUDIO] frames.
     */
    @RequiresApi(Build.VERSION_CODES.Q)
    private class SoundCapture(projection: MediaProjection, private val queue: FrameQueue) {
        private val record: AudioRecord
        @Volatile private var running = true

        init {
            val capture = AudioPlaybackCaptureConfiguration.Builder(projection)
                .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
                .addMatchingUsage(AudioAttributes.USAGE_GAME)
                .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
                .build()
            val format = AudioFormat.Builder()
                .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                .setSampleRate(Screen.AUDIO_RATE)
                .setChannelMask(AudioFormat.CHANNEL_IN_STEREO)
                .build()
            val minimum = AudioRecord.getMinBufferSize(Screen.AUDIO_RATE, AudioFormat.CHANNEL_IN_STEREO, AudioFormat.ENCODING_PCM_16BIT)

            record = build(capture, format, maxOf(minimum, CHUNK * 4))
            record.startRecording()

            thread(name = "flyylink-screen-sound", isDaemon = true) { read() }
        }

        // The service only starts this after checking the permission.
        @SuppressLint("MissingPermission")
        private fun build(capture: AudioPlaybackCaptureConfiguration, format: AudioFormat, size: Int) =
            AudioRecord.Builder()
                .setAudioFormat(format)
                .setBufferSizeInBytes(size)
                .setAudioPlaybackCaptureConfig(capture)
                .build()

        private fun read() {
            val buffer = ByteArray(CHUNK)

            while (running) {
                val count = record.read(buffer, 0, buffer.size)
                if (count < 0) break
                if (count == 0) continue

                // When the Wi-Fi falls behind, late sound is worse than a gap.
                if (queue.backlog > MAX_BACKLOG) continue

                val frame = ScreenFrame(buffer.copyOf(count), SystemClock.elapsedRealtimeNanos() / 1000, ScreenFrame.AUDIO)
                queue.put(frame.header() + frame.data)
            }
        }

        fun stop() {
            running = false
            runCatching { record.stop() }
            record.release()
        }

        private companion object {
            /** 20 ms of 48 kHz 16-bit stereo. */
            const val CHUNK = Screen.AUDIO_RATE / 50 * Screen.AUDIO_CHANNELS * 2
            const val MAX_BACKLOG = 40
        }
    }

    /** Frames waiting to go out, read by the payload connection as one endless stream. */
    private class FrameQueue {
        private val queue = LinkedBlockingQueue<ByteArray>()
        private val end = ByteArray(0)

        val backlog: Int get() = queue.size

        fun put(bytes: ByteArray) = queue.put(bytes)

        fun close() = queue.put(end)

        val stream: InputStream = object : InputStream() {
            private var current: ByteArray? = null
            private var position = 0

            override fun read(): Int {
                val one = ByteArray(1)
                return if (read(one, 0, 1) < 0) -1 else one[0].toInt() and 0xFF
            }

            override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
                var chunk = current
                if (chunk == null || position >= chunk.size) {
                    chunk = queue.take()
                    if (chunk === end) {
                        queue.put(end)
                        return -1
                    }
                    current = chunk
                    position = 0
                }

                val count = minOf(length, chunk.size - position)
                System.arraycopy(chunk, position, buffer, offset, count)
                position += count
                return count
            }
        }
    }

    companion object {
        private const val TAG = "FlyyLinkScreen"
        private const val NOTIFICATION_ID = 5
        private const val ACTION_STOP = "com.toneflix.flyylink.screen.STOP"
        private const val EXTRA_RESULT = "result"
        private const val EXTRA_DATA = "data"
        private const val FRAME_RATE = 60

        /** A size nearly every encoder takes, tried when it turns down the sharper one. */
        private const val SAFE_SIZE = 1600

        @Volatile var isRunning = false
            private set

        fun start(context: Context, resultCode: Int, data: Intent) {
            val intent = Intent(context, ScreenService::class.java)
                .putExtra(EXTRA_RESULT, resultCode)
                .putExtra(EXTRA_DATA, data)
            context.startForegroundService(intent)
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, ScreenService::class.java))
        }
    }
}
