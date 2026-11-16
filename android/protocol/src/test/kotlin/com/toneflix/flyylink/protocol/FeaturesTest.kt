package com.toneflix.flyylink.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class FeaturesTest {
    private fun wire(packet: NetworkPacket) = NetworkPacket.parse(packet.serialize())

    @Test fun unreportedFeaturesAreAllOn() {
        PhoneFeature.entries.forEach { assertTrue(it in PhoneFeatures.ALL) }
        assertTrue(PhoneFeatures.ALL.sends(PacketType.SMS_MESSAGES))
        assertTrue(PhoneFeatures.ALL.accepts(PacketType.SCREEN_INPUT))
    }

    @Test fun featuresThatAreOffBlockTheirPackets() {
        val features = PhoneFeatures.of(PhoneFeature.CLIPBOARD, PhoneFeature.MIRRORING)

        assertTrue(features.sends(PacketType.CLIPBOARD))
        assertTrue(features.accepts(PacketType.SCREEN_REQUEST))
        assertFalse(features.sends(PacketType.NOTIFICATION))
        assertFalse(features.accepts(PacketType.SMS_REQUEST_CONVERSATIONS))
        assertFalse(features.accepts(PacketType.SCREEN_INPUT))
    }

    @Test fun packetsOfNoFeatureAlwaysGo() {
        val none = PhoneFeatures.of()

        listOf(PacketType.PING, PacketType.SHARE_REQUEST, PacketType.FIND_MY_PHONE, PacketType.BATTERY, PacketType.FEATURES).forEach {
            assertTrue(none.sends(it))
            assertTrue(none.accepts(it))
        }
    }

    @Test fun phonesTakeComputerNotificationsWithTheirOwnOff() {
        val none = PhoneFeatures.of()

        assertTrue(none.accepts(PacketType.NOTIFICATION))
        assertTrue(none.sends(PacketType.NOTIFICATION_REQUEST))
    }

    @Test fun packetRoundTripsAndSkipsUnknownNames() {
        val features = PhoneFeatures.of(PhoneFeature.PHOTOS, PhoneFeature.LOCATION)

        assertEquals(features, PhoneFeatures.fromPacket(wire(features.toPacket())))

        val future = NetworkPacket(PacketType.FEATURES, NetworkPacket.body("features" to listOf("photos", "teleport")))
        assertEquals(PhoneFeatures.of(PhoneFeature.PHOTOS), PhoneFeatures.fromPacket(future))
        assertNull(PhoneFeatures.fromPacket(NetworkPacket(PacketType.FEATURES)))
        assertNull(PhoneFeatures.fromPacket(Clipboard.packet("hi")))
    }

    @Test fun identityCarriesFeaturesButTheAnnouncementDoesNot() {
        val identity = DeviceIdentity(
            DeviceIdentity.newDeviceId(), "Pixel 9", DeviceType.PHONE, tcpPort = 1716,
            features = PhoneFeatures.of(PhoneFeature.NOTIFICATIONS),
        )

        assertEquals(identity, DeviceIdentity.fromPacket(wire(identity.toPacket())))
        assertEquals(PhoneFeatures.ALL, DeviceIdentity.fromPacket(wire(identity.announcement())).features)
    }

    @Test fun nothingOnIsNotTheSameAsUnreported() {
        val identity = DeviceIdentity(DeviceIdentity.newDeviceId(), "Pixel 9", DeviceType.PHONE, features = PhoneFeatures.of())
        val heard = DeviceIdentity.fromPacket(wire(identity.toPacket()))

        assertEquals(PhoneFeatures.of(), heard.features)
        assertFalse(PhoneFeature.FILES in heard.features)
    }

    /** The same names as Features.swift and Features.cs. */
    @Test fun wireNamesMatchTheOtherApps() {
        assertEquals(
            listOf("notifications", "media", "messages", "clipboard", "photos", "files", "mirroring", "control", "location"),
            PhoneFeature.entries.map { it.wire },
        )
    }
}
