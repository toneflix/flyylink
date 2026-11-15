package com.toneflix.flyylink

import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.Plugin
import com.toneflix.flyylink.protocol.RemoteInput
import com.toneflix.flyylink.protocol.RemoteInput.MouseButton
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** Each computer's answer to [ComputerRemote.begin]: its platform, and whether it plays our input yet. */
object ComputerRemotePlugin : Plugin {
    override val incoming = setOf(PacketType.INPUT)

    private val _answers = MutableStateFlow<Map<String, RemoteInput.Begin>>(emptyMap())
    val answers: StateFlow<Map<String, RemoteInput.Begin>> = _answers.asStateFlow()

    override fun onPacket(device: Device, packet: NetworkPacket) {
        val answer = RemoteInput.fromPacket(packet) as? RemoteInput.Begin ?: return

        _answers.value += device.id to answer
    }

    /** Whether the device can be worked from this phone. */
    fun canControl(deviceId: String): Boolean {
        val device = DeviceStore.find(deviceId) ?: return false

        return device.link.identity.deviceType.isComputer && PacketType.INPUT in device.link.identity.incomingCapabilities
    }
}

/** Works one computer's pointer and keyboard from this phone ("flyylink.input"). */
class ComputerRemote(private val deviceId: String) {
    val isMac: Boolean get() = ComputerRemotePlugin.answers.value[deviceId]?.platform == RemoteInput.MACOS

    fun begin() = send(RemoteInput.Begin(RemoteInput.ANDROID))

    /** Lets go of anything still held, and hands the computer back. */
    fun release() = send(RemoteInput.Release)

    fun move(dx: Double, dy: Double) = send(RemoteInput.Move(dx, dy))

    fun scroll(dx: Double, dy: Double) = send(RemoteInput.Scroll(dx, dy))

    fun button(button: MouseButton, down: Boolean) = send(RemoteInput.Button(button, down))

    fun click(button: MouseButton = MouseButton.LEFT) {
        button(button, true)
        button(button, false)
    }

    fun type(text: String) = text.chunked(RemoteInput.MAX_TEXT).forEach { send(RemoteInput.Text(it)) }

    /** Presses [code] while holding [modifiers], such as "MetaLeft" and "KeyC" for Command-C. */
    fun press(code: String, modifiers: Collection<String> = emptyList()) {
        modifiers.forEach { send(RemoteInput.Key(it, true)) }
        send(RemoteInput.Key(code, true))
        send(RemoteInput.Key(code, false))
        modifiers.reversed().forEach { send(RemoteInput.Key(it, false)) }
    }

    /** Presses [character] as a key, for shortcuts; false when it has no key of its own. */
    fun press(character: Char, modifiers: Collection<String>): Boolean {
        val (code, shifted) = KeyOf.character(character) ?: return false
        val held = if (shifted) modifiers + SHIFT else modifiers

        press(code, held.distinct())
        return true
    }

    fun nextSlide() = press("ArrowRight")

    fun previousSlide() = press("ArrowLeft")

    /** Starts the show: Keynote's Option-Command-P on a Mac, PowerPoint's F5 on a PC. */
    fun startSlides() = if (isMac) press("KeyP", listOf(META, ALT)) else press("F5")

    fun blackScreen() = press("KeyB")

    fun endSlides() = press("Escape")

    private fun send(input: RemoteInput) {
        DeviceStore.find(deviceId)?.let { DeviceStore.send(it, input.toPacket()) }
    }

    /** The US keyboard key that types a character, and whether it needs Shift. */
    private object KeyOf {
        private val plain = mapOf(
            ' ' to "Space", '-' to "Minus", '=' to "Equal", '[' to "BracketLeft", ']' to "BracketRight", '\\' to "Backslash",
            ';' to "Semicolon", '\'' to "Quote", '`' to "Backquote", ',' to "Comma", '.' to "Period", '/' to "Slash",
            '\n' to "Enter", '\t' to "Tab",
        )
        private val shifted = mapOf(
            '!' to "Digit1", '@' to "Digit2", '#' to "Digit3", '$' to "Digit4", '%' to "Digit5", '^' to "Digit6", '&' to "Digit7",
            '*' to "Digit8", '(' to "Digit9", ')' to "Digit0", '_' to "Minus", '+' to "Equal", '{' to "BracketLeft",
            '}' to "BracketRight", '|' to "Backslash", ':' to "Semicolon", '"' to "Quote", '~' to "Backquote", '<' to "Comma",
            '>' to "Period", '?' to "Slash",
        )

        fun character(character: Char): Pair<String, Boolean>? = when (character) {
            in 'a'..'z' -> "Key${character.uppercaseChar()}" to false
            in 'A'..'Z' -> "Key$character" to true
            in '0'..'9' -> "Digit$character" to false
            in plain -> plain.getValue(character) to false
            in shifted -> shifted.getValue(character) to true
            else -> null
        }
    }

    companion object {
        const val CONTROL = "ControlLeft"
        const val ALT = "AltLeft"
        const val SHIFT = "ShiftLeft"
        const val META = "MetaLeft"

        const val VOLUME_UP = "AudioVolumeUp"
        const val VOLUME_DOWN = "AudioVolumeDown"
        const val MUTE = "AudioVolumeMute"
        const val PLAY_PAUSE = "MediaPlayPause"
        const val NEXT_TRACK = "MediaTrackNext"
        const val PREVIOUS_TRACK = "MediaTrackPrevious"
    }
}
