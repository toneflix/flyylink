package com.toneflix.flyylink.protocol

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer

/**
 * Mirroring the phone's screen on the Mac. The Mac asks with [start]; the phone answers with an
 * "flyylink.screen" packet whose payload is a live H.264 stream of [ScreenFrame]s, sent until either side stops.
 * When the Mac asks for sound and the phone can record it, the stream also carries [ScreenFrame.AUDIO] frames of
 * 16-bit little-endian PCM at [AUDIO_RATE] Hz with [AUDIO_CHANNELS] channels, interleaved.
 */
object Screen {
    /** The payload size of a live stream: read until the sender closes it. */
    const val STREAM = -1L

    /** Windows' H.264 decoder takes at most 2304 pixels on a side. */
    const val MAX_SIZE = 2304
    const val BITRATE = 20_000_000
    const val AUDIO_RATE = 48_000
    const val AUDIO_CHANNELS = 2

    /** How the pictures are encoded. Phones send H.264; computers send each picture as a JPEG. */
    const val H264 = "h264"
    const val JPEG = "jpeg"

    enum class Action(val wire: String) {
        START("start"),
        STOP("stop"),
    }

    data class Request(val action: Action, val maxSize: Int = MAX_SIZE, val bitrate: Int = BITRATE, val audio: Boolean = false)

    /**
     * What the device is sending. Sizes are in pixels; [running] is false once mirroring stops.
     * [audioRate] and [audioChannels] are 0 when the stream has no sound. A computer also says which
     * [platform] it is ([RemoteInput.MACOS] or [RemoteInput.WINDOWS]), so the viewer can swap Control and Command.
     */
    data class Info(
        val width: Int,
        val height: Int,
        val running: Boolean,
        val audioRate: Int = 0,
        val audioChannels: Int = 0,
        val codec: String = H264,
        val platform: String? = null,
    ) {
        val hasAudio: Boolean get() = audioRate > 0 && audioChannels > 0
    }

    fun start(maxSize: Int = MAX_SIZE, bitrate: Int = BITRATE, audio: Boolean = false): NetworkPacket {
        val fields = mutableListOf<Pair<String, Any?>>("action" to Action.START.wire, "maxSize" to maxSize, "bitrate" to bitrate)
        if (audio) fields += "audio" to true
        return NetworkPacket(PacketType.SCREEN_REQUEST, NetworkPacket.body(*fields.toTypedArray()))
    }

    fun stop(): NetworkPacket = NetworkPacket(PacketType.SCREEN_REQUEST, NetworkPacket.body("action" to Action.STOP.wire))

    fun request(packet: NetworkPacket): Request? {
        if (packet.type != PacketType.SCREEN_REQUEST) return null

        val action = Action.entries.firstOrNull { it.wire == packet.string("action") } ?: return null
        return Request(
            action,
            (packet.int("maxSize") ?: MAX_SIZE).coerceIn(320, 4096),
            (packet.int("bitrate") ?: BITRATE).coerceIn(500_000, 50_000_000),
            packet.bool("audio") ?: false,
        )
    }

    /** Announces a stream; send it with [STREAM] as the payload size. */
    fun started(width: Int, height: Int, audio: Boolean = false, codec: String = H264, platform: String? = null): NetworkPacket {
        val fields = mutableListOf<Pair<String, Any?>>("width" to width, "height" to height, "codec" to codec, "running" to true)
        if (platform != null) fields += "platform" to platform
        if (audio) fields += listOf("audioCodec" to "pcm16le", "audioRate" to AUDIO_RATE, "audioChannels" to AUDIO_CHANNELS)
        return NetworkPacket(PacketType.SCREEN, NetworkPacket.body(*fields.toTypedArray()))
    }

    fun stopped(): NetworkPacket = NetworkPacket(PacketType.SCREEN, NetworkPacket.body("running" to false))

    fun info(packet: NetworkPacket): Info? {
        if (packet.type != PacketType.SCREEN) return null

        val pcm = packet.string("audioCodec") == "pcm16le"
        return Info(
            packet.int("width") ?: 0,
            packet.int("height") ?: 0,
            packet.bool("running") ?: false,
            if (pcm) (packet.int("audioRate") ?: 0).coerceIn(0, 192_000) else 0,
            if (pcm) (packet.int("audioChannels") ?: 0).coerceIn(0, 8) else 0,
            packet.string("codec") ?: H264,
            packet.string("platform"),
        )
    }
}

/**
 * One encoded piece of the screen stream. On the wire each is a 13 byte header (size, then the
 * presentation time in microseconds, then [flags]) followed by the H.264 data in Annex B form, or by PCM
 * sound when [isAudio].
 */
class ScreenFrame(val data: ByteArray, val timeUs: Long, val flags: Int) {
    /** The SPS and PPS the decoder needs before any picture. */
    val isConfig: Boolean get() = flags and CONFIG != 0
    val isKey: Boolean get() = flags and KEY != 0
    val isAudio: Boolean get() = flags and AUDIO != 0

    fun header(): ByteArray = ByteBuffer.allocate(HEADER).putInt(data.size).putLong(timeUs).put(flags.toByte()).array()

    override fun equals(other: Any?) =
        other is ScreenFrame && other.timeUs == timeUs && other.flags == flags && other.data.contentEquals(data)

    override fun hashCode() = data.contentHashCode() * 31 + timeUs.hashCode() * 7 + flags

    companion object {
        const val HEADER = 13
        const val CONFIG = 1
        const val KEY = 2
        const val AUDIO = 4

        /** Larger than any real frame; anything bigger means the stream is corrupt. */
        const val MAX_SIZE = 16 * 1024 * 1024
    }

    /** Collects stream bytes, however they are split, back into whole frames. */
    class Reader {
        private val pending = ByteArrayOutputStream()

        fun feed(bytes: ByteArray, offset: Int = 0, length: Int = bytes.size): List<ScreenFrame> {
            pending.write(bytes, offset, length)

            val buffer = ByteBuffer.wrap(pending.toByteArray())
            val frames = mutableListOf<ScreenFrame>()

            while (buffer.remaining() >= HEADER) {
                buffer.mark()
                val size = buffer.int
                if (size < 0 || size > MAX_SIZE) throw ProtocolException("screen frame of $size bytes")

                val time = buffer.long
                val flags = buffer.get().toInt() and 0xFF
                if (buffer.remaining() < size) {
                    buffer.reset()
                    break
                }

                frames += ScreenFrame(ByteArray(size).also { buffer.get(it) }, time, flags)
            }

            pending.reset()
            pending.write(buffer.array(), buffer.position(), buffer.remaining())

            return frames
        }
    }
}

/** A touch, key or typed text from the Mac. Positions are fractions of the screen, 0 to 1 from the top left. */
sealed class ScreenInput {
    data class Tap(val x: Double, val y: Double) : ScreenInput()

    data class LongPress(val x: Double, val y: Double) : ScreenInput()

    data class Swipe(val x: Double, val y: Double, val toX: Double, val toY: Double, val durationMs: Long) : ScreenInput()

    data class Text(val text: String) : ScreenInput()

    /** A key that is not text. With [select], a movement key extends the selection like Shift does. */
    data class Key(val key: String, val select: Boolean = false) : ScreenInput()

    fun toPacket(): NetworkPacket = NetworkPacket(
        PacketType.SCREEN_INPUT,
        when (this) {
            is Tap -> NetworkPacket.body("action" to "tap", "x" to x, "y" to y)
            is LongPress -> NetworkPacket.body("action" to "longPress", "x" to x, "y" to y)
            is Swipe -> NetworkPacket.body("action" to "swipe", "x" to x, "y" to y, "toX" to toX, "toY" to toY, "duration" to durationMs)
            is Text -> NetworkPacket.body("action" to "text", "text" to text)
            is Key -> if (select) {
                NetworkPacket.body("action" to "key", "key" to key, "select" to true)
            } else {
                NetworkPacket.body("action" to "key", "key" to key)
            }
        },
    )

    companion object {
        const val BACK = "back"
        const val HOME = "home"
        const val RECENTS = "recents"
        const val NOTIFICATIONS = "notifications"
        const val ENTER = "enter"
        const val DELETE = "delete"
        const val DELETE_FORWARD = "deleteForward"
        const val DELETE_WORD = "deleteWord"
        const val TAB = "tab"
        const val LEFT = "left"
        const val RIGHT = "right"
        const val UP = "up"
        const val DOWN = "down"
        const val WORD_LEFT = "wordLeft"
        const val WORD_RIGHT = "wordRight"
        const val LINE_START = "lineStart"
        const val LINE_END = "lineEnd"
        const val SELECT_ALL = "selectAll"
        const val COPY = "copy"
        const val CUT = "cut"

        val keys = setOf(
            BACK, HOME, RECENTS, NOTIFICATIONS, ENTER, DELETE, DELETE_FORWARD, DELETE_WORD, TAB,
            LEFT, RIGHT, UP, DOWN, WORD_LEFT, WORD_RIGHT, LINE_START, LINE_END, SELECT_ALL, COPY, CUT,
        )

        fun fromPacket(packet: NetworkPacket): ScreenInput? {
            if (packet.type != PacketType.SCREEN_INPUT) return null

            fun fraction(key: String) = packet.double(key)?.takeIf { it in 0.0..1.0 }

            return when (packet.string("action")) {
                "tap" -> Tap(fraction("x") ?: return null, fraction("y") ?: return null)
                "longPress" -> LongPress(fraction("x") ?: return null, fraction("y") ?: return null)
                "swipe" -> Swipe(
                    fraction("x") ?: return null,
                    fraction("y") ?: return null,
                    fraction("toX") ?: return null,
                    fraction("toY") ?: return null,
                    (packet.long("duration") ?: 200).coerceIn(10, 5000),
                )
                "text" -> packet.string("text")?.takeIf { it.isNotEmpty() }?.let(::Text)
                "key" -> packet.string("key")?.takeIf { it in keys }?.let { Key(it, packet.bool("select") ?: false) }
                else -> null
            }
        }
    }
}
