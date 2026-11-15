package com.toneflix.flyylink.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PhoneContactsTest {
    private fun wire(packet: NetworkPacket) = NetworkPacket.parse(packet.serialize())

    private val ada = PhoneContact("Ada Obi", listOf("+234 803 555 0101", "0803-555-0102"))
    private val bayo = PhoneContact("Bayo", listOf("08125550199"))
    private val nada = PhoneContact("Nada", listOf("+44 20 7946 0000"))

    @Test fun contactsRoundTrip() {
        val contacts = listOf(ada, bayo)

        assertEquals(contacts, PhoneContacts.contacts(wire(PhoneContacts.packet(contacts))))
        assertTrue(PhoneContacts.isRequest(wire(PhoneContacts.request())))
    }

    @Test fun dropsContactsWithoutNameOrNumber() {
        val packet = NetworkPacket.parse("""{"id":1,"type":"flyylink.contacts","body":{"contacts":[{"name":"","numbers":["1"]},{"name":"Ann","numbers":[]},{"name":"Bo","numbers":[" 2 "]}]}}""")

        assertEquals(listOf(PhoneContact("Bo", listOf("2"))), PhoneContacts.contacts(packet))
    }

    @Test fun matchesNamesAndNumbers() {
        assertTrue(ada.matches("obi"))
        assertTrue(ada.matches("555 0102"))
        assertTrue(ada.matches(""))
        assertFalse(ada.matches("Bayo"))
        assertFalse(ada.matches("+"))
    }

    @Test fun searchPutsNameStartsFirst() {
        assertEquals(listOf(ada, nada), PhoneContacts.search(listOf(nada, bayo, ada), "ada"))
        assertEquals(listOf(bayo), PhoneContacts.search(listOf(nada, bayo, ada), "0812"))
    }

    @Test fun phonesAndComputersShareContactsButTvsDont() {
        assertTrue(PacketType.CONTACTS in Capabilities.mvp)
        assertFalse(PacketType.CONTACTS_REQUEST in Capabilities.tv)
    }
}
