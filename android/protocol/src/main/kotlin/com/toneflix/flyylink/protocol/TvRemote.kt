package com.toneflix.flyylink.protocol

/**
 * A press on the remote a phone shows for a TV ("flyylink.remote"): a [Key], [Text] typed into the TV's
 * focused field, or the touchpad's pointer moves, clicks and scrolls. Moves and scrolls are in the phone's dp.
 * Mirrors TvRemote.swift and TvRemote.cs.
 */
sealed class TvRemote {
    data class Key(val key: String) : TvRemote()

    data class Text(val text: String) : TvRemote()

    data class Move(val dx: Double, val dy: Double) : TvRemote()

    data object Click : TvRemote()

    data class Scroll(val dx: Double, val dy: Double) : TvRemote()

    fun toPacket(): NetworkPacket = NetworkPacket(
        PacketType.REMOTE,
        when (this) {
            is Key -> NetworkPacket.body("action" to "key", "key" to key)
            is Text -> NetworkPacket.body("action" to "text", "text" to text)
            is Move -> NetworkPacket.body("action" to "move", "dx" to dx, "dy" to dy)
            is Click -> NetworkPacket.body("action" to "click")
            is Scroll -> NetworkPacket.body("action" to "scroll", "dx" to dx, "dy" to dy)
        },
    )

    companion object {
        const val UP = "up"
        const val DOWN = "down"
        const val LEFT = "left"
        const val RIGHT = "right"
        const val SELECT = "select"
        const val BACK = "back"
        const val HOME = "home"
        const val MENU = "menu"
        const val PLAY_PAUSE = "playPause"
        const val NEXT = "next"
        const val PREVIOUS = "previous"
        const val VOLUME_UP = "volumeUp"
        const val VOLUME_DOWN = "volumeDown"
        const val MUTE = "mute"
        const val DELETE = "delete"
        const val ENTER = "enter"

        val keys = setOf(UP, DOWN, LEFT, RIGHT, SELECT, BACK, HOME, MENU, PLAY_PAUSE, NEXT, PREVIOUS, VOLUME_UP, VOLUME_DOWN, MUTE, DELETE, ENTER)

        /** Longer text than this in one packet is treated as nonsense. */
        const val MAX_TEXT = 1000

        /** A bigger move or scroll than this in one packet is treated as nonsense. */
        const val MAX_STEP = 2000.0

        fun fromPacket(packet: NetworkPacket): TvRemote? {
            if (packet.type != PacketType.REMOTE) return null

            return when (packet.string("action")) {
                "key" -> packet.string("key")?.takeIf { it in keys }?.let(::Key)
                "text" -> packet.string("text")?.takeIf { it.isNotEmpty() && it.length <= MAX_TEXT }?.let(::Text)
                "move" -> step(packet, ::Move)
                "click" -> Click
                "scroll" -> step(packet, ::Scroll)
                else -> null
            }
        }

        private fun step(packet: NetworkPacket, make: (Double, Double) -> TvRemote): TvRemote? {
            val dx = packet.double("dx")?.takeIf { it.isFinite() && kotlin.math.abs(it) <= MAX_STEP } ?: return null
            val dy = packet.double("dy")?.takeIf { it.isFinite() && kotlin.math.abs(it) <= MAX_STEP } ?: return null

            return make(dx, dy)
        }
    }
}
