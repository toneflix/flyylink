package com.toneflix.flyylink.protocol

/**
 * A phone feature the user turns on in the app. [sends] are the packets the phone sends for it and
 * [accepts] the ones it takes from computers, so neither goes through while the feature is off.
 */
enum class PhoneFeature(val wire: String, val sends: Set<String>, val accepts: Set<String>) {
    NOTIFICATIONS(
        "notifications",
        sends = setOf(PacketType.NOTIFICATION),
        accepts = setOf(PacketType.NOTIFICATION_REQUEST, PacketType.NOTIFICATION_REPLY, PacketType.NOTIFICATION_ACTION),
    ),
    MEDIA("media", sends = setOf(PacketType.MPRIS), accepts = setOf(PacketType.MPRIS_REQUEST)),
    MESSAGES(
        "messages",
        sends = setOf(PacketType.SMS_MESSAGES, PacketType.TELEPHONY, PacketType.CONTACTS, PacketType.CALL_AUDIO),
        accepts = setOf(
            PacketType.SMS_REQUEST,
            PacketType.SMS_REQUEST_CONVERSATIONS,
            PacketType.SMS_REQUEST_CONVERSATION,
            PacketType.CALL_REQUEST,
            PacketType.CONTACTS_REQUEST,
            PacketType.CALL_AUDIO_REQUEST,
        ),
    ),
    CLIPBOARD(
        "clipboard",
        sends = setOf(PacketType.CLIPBOARD, PacketType.CLIPBOARD_CONNECT, PacketType.CLIPBOARD_FILE),
        accepts = setOf(PacketType.CLIPBOARD, PacketType.CLIPBOARD_CONNECT, PacketType.CLIPBOARD_FILE),
    ),
    PHOTOS(
        "photos",
        sends = setOf(PacketType.PHOTOS, PacketType.PHOTOS_FILE),
        accepts = setOf(PacketType.PHOTOS_REQUEST, PacketType.PHOTOS_DOWNLOAD),
    ),
    FILES("files", sends = setOf(PacketType.FILES), accepts = setOf(PacketType.FILES_REQUEST)),
    MIRRORING("mirroring", sends = setOf(PacketType.SCREEN), accepts = setOf(PacketType.SCREEN_REQUEST)),
    CONTROL("control", sends = emptySet(), accepts = setOf(PacketType.SCREEN_INPUT)),
    LOCATION("location", sends = setOf(PacketType.LOCATION), accepts = setOf(PacketType.LOCATION_REQUEST));

    companion object {
        fun fromWire(value: String): PhoneFeature? = entries.firstOrNull { it.wire == value }
    }
}

/**
 * The features a device has on. [reported] is null when the device doesn't say, as computers, TVs and
 * older phones don't, and then every feature counts as on.
 */
data class PhoneFeatures(val reported: Set<PhoneFeature>?) {
    operator fun contains(feature: PhoneFeature): Boolean = reported?.contains(feature) ?: true

    /** False when [type] belongs to a feature that is off on the sending side. */
    fun sends(type: String): Boolean = PhoneFeature.entries.none { type in it.sends && it !in this }

    /** False when [type] belongs to a feature that is off on the receiving side. */
    fun accepts(type: String): Boolean = PhoneFeature.entries.none { type in it.accepts && it !in this }

    /** Tells paired devices which features are on, after one changes. */
    fun toPacket(): NetworkPacket = NetworkPacket(PacketType.FEATURES, NetworkPacket.body(BODY_KEY to wire()))

    /** The wire names of the features that are on, for the identity and [toPacket]; null when not reported. */
    fun wire(): List<String>? = reported?.map { it.wire }?.sorted()

    companion object {
        const val BODY_KEY = "features"

        val ALL = PhoneFeatures(null)

        fun of(vararg features: PhoneFeature) = PhoneFeatures(features.toSet())

        /** Reads the list in an identity or [PacketType.FEATURES] packet. Unknown names are skipped. */
        fun fromBody(packet: NetworkPacket): PhoneFeatures {
            if (!packet.body.containsKey(BODY_KEY)) return ALL

            return PhoneFeatures(packet.stringList(BODY_KEY).mapNotNullTo(mutableSetOf()) { PhoneFeature.fromWire(it) })
        }

        fun fromPacket(packet: NetworkPacket): PhoneFeatures? =
            if (packet.type == PacketType.FEATURES && packet.body.containsKey(BODY_KEY)) fromBody(packet) else null
    }
}
