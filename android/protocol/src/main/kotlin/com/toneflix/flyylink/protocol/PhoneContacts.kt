package com.toneflix.flyylink.protocol

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

/** A person in the phone's contacts with the numbers they can be texted or called on. */
data class PhoneContact(val name: String, val numbers: List<String>) {
    fun toJson(): JsonObject = buildJsonObject {
        put("name", name)
        put("numbers", JsonArray(numbers.map(::JsonPrimitive)))
    }

    /** Whether [query] is part of the name, ignoring case, or its digits are part of a number. */
    fun matches(query: String): Boolean {
        val text = query.trim()
        if (text.isEmpty()) return true
        if (name.contains(text, ignoreCase = true)) return true

        val digits = PhoneContacts.digits(text)
        return digits.isNotEmpty() && numbers.any { PhoneContacts.digits(it).contains(digits) }
    }

    companion object {
        fun fromJson(json: JsonObject): PhoneContact? {
            val name = (json["name"] as? JsonPrimitive)?.takeIf { it.isString }?.content?.trim().orEmpty()
            val numbers = (json["numbers"] as? JsonArray).orEmpty()
                .mapNotNull { (it as? JsonPrimitive)?.takeIf { value -> value.isString }?.content?.trim() }
                .filter { it.isNotEmpty() }
            if (name.isEmpty() || numbers.isEmpty()) return null

            return PhoneContact(name, numbers)
        }
    }
}

/** The phone's contacts for a computer's Messages window: it sends [PacketType.CONTACTS_REQUEST], the phone answers [PacketType.CONTACTS]. */
object PhoneContacts {
    fun request(): NetworkPacket = NetworkPacket(PacketType.CONTACTS_REQUEST)

    fun isRequest(packet: NetworkPacket): Boolean = packet.type == PacketType.CONTACTS_REQUEST

    fun packet(contacts: List<PhoneContact>): NetworkPacket = NetworkPacket(
        PacketType.CONTACTS,
        buildJsonObject { put("contacts", JsonArray(contacts.map { it.toJson() })) },
    )

    fun contacts(packet: NetworkPacket): List<PhoneContact>? {
        if (packet.type != PacketType.CONTACTS) return null

        return (packet.body["contacts"] as? JsonArray).orEmpty().mapNotNull { (it as? JsonObject)?.let(PhoneContact::fromJson) }
    }

    /** The contacts that match [query], those whose name starts with it first, then by name. */
    fun search(contacts: List<PhoneContact>, query: String): List<PhoneContact> {
        val text = query.trim()

        return contacts.filter { it.matches(text) }
            .sortedWith(compareBy({ !it.name.startsWith(text, ignoreCase = true) }, { it.name.lowercase() }))
    }

    fun digits(text: String): String = text.filter { it.isDigit() }
}
