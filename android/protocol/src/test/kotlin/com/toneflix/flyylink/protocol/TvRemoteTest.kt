package com.toneflix.flyylink.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TvRemoteTest {
    private fun wire(packet: NetworkPacket) = NetworkPacket.parse(packet.serialize())

    @Test fun keysAndTextRoundTrip() {
        TvRemote.keys.forEach { assertEquals(TvRemote.Key(it), TvRemote.fromPacket(wire(TvRemote.Key(it).toPacket()))) }
        assertEquals(TvRemote.Text("hello"), TvRemote.fromPacket(wire(TvRemote.Text("hello").toPacket())))
        assertEquals("playPause", TvRemote.Key(TvRemote.PLAY_PAUSE).toPacket().string("key"))
    }

    @Test fun pointerRoundTrips() {
        assertEquals(TvRemote.Move(12.5, -3.0), TvRemote.fromPacket(wire(TvRemote.Move(12.5, -3.0).toPacket())))
        assertEquals(TvRemote.Click, TvRemote.fromPacket(wire(TvRemote.Click.toPacket())))
        assertEquals(TvRemote.Scroll(0.0, 40.0), TvRemote.fromPacket(wire(TvRemote.Scroll(0.0, 40.0).toPacket())))
        assertNull(TvRemote.fromPacket(wire(TvRemote.Move(TvRemote.MAX_STEP + 1, 0.0).toPacket())))
        assertNull(TvRemote.fromPacket(NetworkPacket(PacketType.REMOTE, NetworkPacket.body("action" to "move", "dx" to 1.0))))
    }

    @Test fun rejectsNonsense() {
        assertNull(TvRemote.fromPacket(wire(TvRemote.Key("launchMissiles").toPacket())))
        assertNull(TvRemote.fromPacket(wire(TvRemote.Text("").toPacket())))
        assertNull(TvRemote.fromPacket(wire(TvRemote.Text("x".repeat(TvRemote.MAX_TEXT + 1)).toPacket())))
        assertNull(TvRemote.fromPacket(NetworkPacket(PacketType.REMOTE, NetworkPacket.body("action" to "fly"))))
        assertNull(TvRemote.fromPacket(NetworkPacket(PacketType.PING)))
    }

    @Test fun tvsSkipPhoneAndComputerFeatures() {
        assertTrue(PacketType.REMOTE in Capabilities.mvp)
        assertTrue(Capabilities.tv.containsAll(listOf(PacketType.REMOTE, PacketType.SHARE_REQUEST, PacketType.NOTIFICATION, PacketType.MPRIS_REQUEST, PacketType.FLYYDROP_REQUEST)))
        assertFalse(PacketType.SMS_REQUEST in Capabilities.tv)
        assertFalse(PacketType.TELEPHONY in Capabilities.tv)
        assertFalse(PacketType.CLIPBOARD in Capabilities.tv)
        assertFalse(PacketType.SCREEN_REQUEST in Capabilities.tv)
        assertFalse(PacketType.BATTERY in Capabilities.tv)
        assertTrue(Capabilities.mvp.containsAll(Capabilities.tv))
    }

    @Test fun capabilitiesFollowTheDeviceType() {
        assertEquals(Capabilities.tv, Capabilities.forType(DeviceType.TV))
        assertEquals(Capabilities.mvp, Capabilities.forType(DeviceType.PHONE))
        assertEquals(Capabilities.mvp, Capabilities.forType(DeviceType.LAPTOP))
    }
}
