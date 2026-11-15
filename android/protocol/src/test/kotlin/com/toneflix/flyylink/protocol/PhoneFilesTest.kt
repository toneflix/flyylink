package com.toneflix.flyylink.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PhoneFilesTest {
    private fun wire(packet: NetworkPacket) = NetworkPacket.parse(packet.serialize())

    @Test fun cleansPaths() {
        assertEquals("/", PhoneFiles.clean(""))
        assertEquals("/", PhoneFiles.clean("/"))
        assertEquals("/DCIM/Camera", PhoneFiles.clean("DCIM//./Camera/"))
        assertNull(PhoneFiles.clean("/DCIM/../Android"))
        assertNull(PhoneFiles.clean("/a\u0000b"))
    }

    @Test fun splitsPaths() {
        assertEquals("/Music", PhoneFiles.join("/", "Music"))
        assertEquals("/Music/a.mp3", PhoneFiles.join("/Music", "a.mp3"))
        assertEquals("a.mp3", PhoneFiles.name("/Music/a.mp3"))
        assertEquals("/Music", PhoneFiles.parent("/Music/a.mp3"))
        assertEquals("/", PhoneFiles.parent("/Music"))
        assertTrue(PhoneFiles.isValidName("Notes.txt"))
        assertFalse(PhoneFiles.isValidName(".."))
        assertFalse(PhoneFiles.isValidName("a/b"))
    }

    @Test fun requestsRoundTrip() {
        val list = PhoneFiles.Request(7, PhoneFiles.Action.LIST, "/DCIM")
        val move = PhoneFiles.Request(8, PhoneFiles.Action.MOVE, "/a.txt", "/Documents/a.txt")

        assertEquals(list, PhoneFiles.request(wire(PhoneFiles.request(list))))
        assertEquals(move, PhoneFiles.request(wire(PhoneFiles.request(move))))
    }

    @Test fun refusesBadRequests() {
        assertNull(PhoneFiles.request(wire(PhoneFiles.request(PhoneFiles.Request(1, PhoneFiles.Action.LIST, "/../etc")))))
        assertNull(PhoneFiles.request(wire(PhoneFiles.request(PhoneFiles.Request(1, PhoneFiles.Action.MOVE, "/a")))))
        assertNull(PhoneFiles.request(wire(PhoneFiles.request(PhoneFiles.Request(1, PhoneFiles.Action.MOVE, "/a", "/")))))
        assertNull(PhoneFiles.request(NetworkPacket(PacketType.FILES_REQUEST, NetworkPacket.body("id" to 1, "action" to "format", "path" to "/"))))
    }

    @Test fun answersRoundTrip() {
        val files = listOf(
            PhoneFile("/DCIM", isFolder = true, modified = 1_700_000_000_000),
            PhoneFile("/notes.txt", isFolder = false, size = 12, modified = 1_700_000_100_000),
        )
        val listing = PhoneFiles.Answer(7, files = files)
        val sent = PhoneFiles.Answer(8, file = files[1])
        val failed = PhoneFiles.Answer(9, failure = PhoneFiles.Failure.DENIED)

        assertEquals(listing, PhoneFiles.answer(wire(PhoneFiles.answer(listing))))
        assertEquals(sent, PhoneFiles.answer(wire(PhoneFiles.answer(sent))))
        assertEquals(failed, PhoneFiles.answer(wire(PhoneFiles.answer(failed))))
        assertEquals("notes.txt", files[1].name)
    }

    @Test fun readsUnknownFailureAsFailed() {
        val packet = NetworkPacket.parse("""{"id":1,"type":"flyylink.files","body":{"id":3,"ok":false,"error":"disk on fire"}}""")

        assertEquals(PhoneFiles.Failure.FAILED, PhoneFiles.answer(packet)?.failure)
    }

    @Test fun phonesAdvertiseFilesButTvsDont() {
        assertTrue(PacketType.FILES_REQUEST in Capabilities.mvp)
        assertFalse(PacketType.FILES_REQUEST in Capabilities.tv)
    }
}
