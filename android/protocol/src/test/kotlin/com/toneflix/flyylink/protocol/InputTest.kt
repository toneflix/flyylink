package com.toneflix.flyylink.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class InputTest {
    @Test fun everyInputRoundTrips() {
        val inputs = listOf(
            RemoteInput.Enter(ScreenEdge.LEFT, 0.25, RemoteInput.WINDOWS),
            RemoteInput.Leave(ScreenEdge.BOTTOM, 1.0),
            RemoteInput.Release,
            RemoteInput.Move(-3.5, 12.0),
            RemoteInput.MoveTo(0.5, 0.0),
            RemoteInput.Button(RemoteInput.MouseButton.RIGHT, true),
            RemoteInput.Scroll(0.0, -40.0),
            RemoteInput.Key("KeyA", false),
            RemoteInput.Key("MetaLeft", true),
            RemoteInput.Key("AudioVolumeUp", true),
            RemoteInput.Begin(RemoteInput.ANDROID),
            RemoteInput.Begin(RemoteInput.MACOS, ready = false),
            RemoteInput.Text("Héllo 👋"),
        )

        for (input in inputs) {
            assertEquals(input, RemoteInput.fromPacket(NetworkPacket.parse(input.toPacket().serialize())))
        }
    }

    @Test fun rejectsNonsense() {
        fun packet(vararg pairs: Pair<String, Any?>) = NetworkPacket(PacketType.INPUT, NetworkPacket.body(*pairs))

        assertNull(RemoteInput.fromPacket(packet("action" to "enter", "edge" to "middle", "position" to 0.5)))
        assertNull(RemoteInput.fromPacket(packet("action" to "moveTo", "x" to 1.5, "y" to 0.5)))
        assertNull(RemoteInput.fromPacket(packet("action" to "move", "dx" to 50_000, "dy" to 0)))
        assertNull(RemoteInput.fromPacket(packet("action" to "key", "code" to "Hyper", "down" to true)))
        assertNull(RemoteInput.fromPacket(packet("action" to "button", "button" to "left")))
        assertNull(RemoteInput.fromPacket(packet("action" to "text", "text" to "")))
        assertNull(RemoteInput.fromPacket(packet("action" to "text", "text" to "a".repeat(RemoteInput.MAX_TEXT + 1))))
        assertNull(RemoteInput.fromPacket(packet("action" to "dance")))
    }

    @Test fun beginWithoutReadyMeansReady() {
        val packet = NetworkPacket(PacketType.INPUT, NetworkPacket.body("action" to "begin", "platform" to RemoteInput.WINDOWS))

        assertEquals(RemoteInput.Begin(RemoteInput.WINDOWS, ready = true), RemoteInput.fromPacket(packet))
    }

    @Test fun edgesMapPointsBothWays() {
        assertEquals(ScreenEdge.RIGHT, ScreenEdge.LEFT.opposite)
        assertEquals(ScreenEdge.BOTTOM, ScreenEdge.TOP.opposite)

        assertEquals(0.25, ScreenEdge.RIGHT.position(1920.0, 270.0, 0.0, 0.0, 1920.0, 1080.0), 0.0)
        assertEquals(0.0, ScreenEdge.BOTTOM.position(-10.0, 1080.0, 0.0, 0.0, 1920.0, 1080.0), 0.0)
        assertEquals(Pair(2.0, 450.0), ScreenEdge.LEFT.point(0.5, 0.0, 0.0, 1440.0, 900.0))

        assertTrue(ScreenEdge.TOP.reached(100.0, 0.0, 0.0, 0.0, 1440.0, 900.0))
        assertFalse(ScreenEdge.RIGHT.reached(1000.0, 0.0, 0.0, 0.0, 1440.0, 900.0))
    }

    @Test fun keyNamesCoverTheKeyboard() {
        assertTrue("KeyZ" in KeyCodes.all)
        assertTrue("Digit0" in KeyCodes.all)
        assertTrue("F12" in KeyCodes.all)
        assertTrue("NumpadEnter" in KeyCodes.all)
        assertTrue("MediaPlayPause" in KeyCodes.all)
        assertEquals("MetaLeft", KeyCodes.swappingControlAndMeta("ControlLeft"))
        assertEquals("KeyC", KeyCodes.swappingControlAndMeta("KeyC"))
    }
}
