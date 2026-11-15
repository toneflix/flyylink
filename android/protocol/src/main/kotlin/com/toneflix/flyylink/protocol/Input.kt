package com.toneflix.flyylink.protocol

import kotlin.math.abs

/** A side of a computer's screen, where the pointer crosses to another computer. */
enum class ScreenEdge(val wire: String) {
    LEFT("left"), RIGHT("right"), TOP("top"), BOTTOM("bottom");

    val opposite: ScreenEdge
        get() = when (this) {
            LEFT -> RIGHT
            RIGHT -> LEFT
            TOP -> BOTTOM
            BOTTOM -> TOP
        }

    /** Where along this edge a point is, from 0 at the top or left to 1 at the bottom or right. */
    fun position(x: Double, y: Double, minX: Double, minY: Double, maxX: Double, maxY: Double): Double {
        val fraction = when (this) {
            LEFT, RIGHT -> if (maxY > minY) (y - minY) / (maxY - minY) else 0.5
            TOP, BOTTOM -> if (maxX > minX) (x - minX) / (maxX - minX) else 0.5
        }
        return fraction.coerceIn(0.0, 1.0)
    }

    /** The point just inside this edge at [position], [inset] away from it. */
    fun point(position: Double, minX: Double, minY: Double, maxX: Double, maxY: Double, inset: Double = 2.0): Pair<Double, Double> {
        val along = position.coerceIn(0.0, 1.0)
        return when (this) {
            LEFT -> Pair(minX + inset, minY + along * (maxY - minY))
            RIGHT -> Pair(maxX - inset, minY + along * (maxY - minY))
            TOP -> Pair(minX + along * (maxX - minX), minY + inset)
            BOTTOM -> Pair(minX + along * (maxX - minX), maxY - inset)
        }
    }

    /** True when the point is at or past this edge. */
    fun reached(x: Double, y: Double, minX: Double, minY: Double, maxX: Double, maxY: Double): Boolean = when (this) {
        LEFT -> x <= minX
        RIGHT -> x >= maxX
        TOP -> y <= minY
        BOTTOM -> y >= maxY
    }

    companion object {
        fun fromWire(value: String?): ScreenEdge? = entries.firstOrNull { it.wire == value }
    }
}

/**
 * Keyboard and mouse shared from one computer to another ("flyylink.input"). The controlling computer sends [Enter]
 * when its pointer leaves through an edge, then moves, buttons, scrolls and keys. The controlled computer sends
 * [Leave] when its pointer goes back out through the edge it came in by. A phone sends [Begin] instead and works the
 * computer with its touchpad and keyboard. Keys use the W3C `code` names, such as "KeyA" or "ShiftLeft".
 * Mirrors Input.swift and Input.cs.
 */
sealed class RemoteInput {
    /** The pointer arrives at the receiver's [edge], [position] along it. [platform] is the sender's. */
    data class Enter(val edge: ScreenEdge, val position: Double, val platform: String) : RemoteInput()

    /**
     * The sender works the receiver without an edge, as a phone does. The receiver answers with its own [Begin], so
     * the sender learns its [platform], and [ready] is false while it can't play input.
     */
    data class Begin(val platform: String, val ready: Boolean = true) : RemoteInput()

    /** The pointer goes back to the sender, leaving the receiver through [edge] at [position]. */
    data class Leave(val edge: ScreenEdge, val position: Double) : RemoteInput()

    /** The sender stopped controlling; let go of anything still held. */
    data object Release : RemoteInput()

    data class Move(val dx: Double, val dy: Double) : RemoteInput()

    /** Moves the pointer to a fraction of the receiver's main screen, 0 to 1 from the top left. */
    data class MoveTo(val x: Double, val y: Double) : RemoteInput()

    data class Button(val button: MouseButton, val down: Boolean) : RemoteInput()

    /** Scrolls by this many points; positive [dy] scrolls up and positive [dx] right. */
    data class Scroll(val dx: Double, val dy: Double) : RemoteInput()

    data class Key(val code: String, val down: Boolean) : RemoteInput()

    /** Types [text] as it is, whatever the receiver's keyboard layout. */
    data class Text(val text: String) : RemoteInput()

    enum class MouseButton(val wire: String) {
        LEFT("left"), RIGHT("right"), MIDDLE("middle");

        companion object {
            fun fromWire(value: String?): MouseButton? = entries.firstOrNull { it.wire == value }
        }
    }

    fun toPacket(): NetworkPacket = NetworkPacket(
        PacketType.INPUT,
        when (this) {
            is Enter -> NetworkPacket.body("action" to "enter", "edge" to edge.wire, "position" to position, "platform" to platform)
            is Begin -> NetworkPacket.body("action" to "begin", "platform" to platform, "ready" to ready)
            is Leave -> NetworkPacket.body("action" to "leave", "edge" to edge.wire, "position" to position)
            Release -> NetworkPacket.body("action" to "release")
            is Move -> NetworkPacket.body("action" to "move", "dx" to dx, "dy" to dy)
            is MoveTo -> NetworkPacket.body("action" to "moveTo", "x" to x, "y" to y)
            is Button -> NetworkPacket.body("action" to "button", "button" to button.wire, "down" to down)
            is Scroll -> NetworkPacket.body("action" to "scroll", "dx" to dx, "dy" to dy)
            is Key -> NetworkPacket.body("action" to "key", "code" to code, "down" to down)
            is Text -> NetworkPacket.body("action" to "text", "text" to text)
        },
    )

    companion object {
        const val MACOS = "macos"
        const val WINDOWS = "windows"
        const val ANDROID = "android"

        /** Larger moves or scrolls than this in one packet are treated as nonsense. */
        private const val MAX_STEP = 10_000.0

        /** Longer text than this in one packet is treated as nonsense. */
        const val MAX_TEXT = 1000

        fun fromPacket(packet: NetworkPacket): RemoteInput? {
            if (packet.type != PacketType.INPUT) return null

            fun fraction(key: String) = packet.double(key)?.takeIf { it in 0.0..1.0 }
            fun step(key: String) = packet.double(key)?.takeIf { it.isFinite() && abs(it) <= MAX_STEP }

            return when (packet.string("action")) {
                "enter" -> Enter(
                    ScreenEdge.fromWire(packet.string("edge")) ?: return null,
                    fraction("position") ?: return null,
                    packet.string("platform") ?: "",
                )
                "begin" -> Begin(packet.string("platform") ?: "", packet.bool("ready") ?: true)
                "leave" -> Leave(ScreenEdge.fromWire(packet.string("edge")) ?: return null, fraction("position") ?: return null)
                "release" -> Release
                "move" -> Move(step("dx") ?: return null, step("dy") ?: return null)
                "moveTo" -> MoveTo(fraction("x") ?: return null, fraction("y") ?: return null)
                "button" -> Button(MouseButton.fromWire(packet.string("button")) ?: return null, packet.bool("down") ?: return null)
                "scroll" -> Scroll(step("dx") ?: return null, step("dy") ?: return null)
                "key" -> Key(packet.string("code")?.takeIf { it in KeyCodes.all } ?: return null, packet.bool("down") ?: return null)
                "text" -> Text(packet.string("text")?.takeIf { it.isNotEmpty() && it.length <= MAX_TEXT } ?: return null)
                else -> null
            }
        }
    }
}

/** The W3C `code` names keys travel under, and the ones that act as modifiers. */
object KeyCodes {
    val letters = ('A'..'Z').map { "Key$it" }
    val digits = (0..9).map { "Digit$it" }
    val functionKeys = (1..12).map { "F$it" }
    val numpad = (0..9).map { "Numpad$it" } + listOf("NumpadDecimal", "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadEnter")
    val modifiers = setOf("ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight")
    val others = listOf(
        "Enter", "Escape", "Backspace", "Tab", "Space", "Minus", "Equal", "BracketLeft", "BracketRight", "Backslash",
        "Semicolon", "Quote", "Backquote", "Comma", "Period", "Slash", "IntlBackslash", "CapsLock",
        "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown", "Delete", "Insert", "ContextMenu",
    )
    val media = listOf("AudioVolumeUp", "AudioVolumeDown", "AudioVolumeMute", "MediaPlayPause", "MediaTrackNext", "MediaTrackPrevious")

    val all: Set<String> = (letters + digits + functionKeys + numpad + others + media).toSet() + modifiers

    /** Swaps Control and Meta, so Ctrl+C on a PC is Command-C on a Mac and the other way round. */
    fun swappingControlAndMeta(code: String): String = when (code) {
        "ControlLeft" -> "MetaLeft"
        "ControlRight" -> "MetaRight"
        "MetaLeft" -> "ControlLeft"
        "MetaRight" -> "ControlRight"
        else -> code
    }
}
