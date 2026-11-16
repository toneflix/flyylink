package com.toneflix.flyylink.protocol

import java.util.UUID

enum class DeviceType(val wire: String) {
    DESKTOP("desktop"), LAPTOP("laptop"), PHONE("phone"), TABLET("tablet"), TV("tv");

    /** A Mac or PC rather than a phone, tablet or TV. */
    val isComputer: Boolean get() = this == DESKTOP || this == LAPTOP

    val isPhone: Boolean get() = this == PHONE || this == TABLET

    /** Phones never pair with each other; they only reach one another through FlyyDrop. */
    fun pairsWith(other: DeviceType): Boolean = !(isPhone && other.isPhone)

    companion object {
        fun fromWire(value: String?): DeviceType = entries.firstOrNull { it.wire == value } ?: DESKTOP
    }
}

data class DeviceIdentity(
    val deviceId: String,
    val deviceName: String,
    val deviceType: DeviceType,
    val protocolVersion: Int = PROTOCOL_VERSION,
    val incomingCapabilities: Set<String> = Capabilities.mvp,
    val outgoingCapabilities: Set<String> = Capabilities.mvp,
    val tcpPort: Int? = null,
    /** The phone features the user has on. Devices that don't report them get [PhoneFeatures.ALL]. */
    val features: PhoneFeatures = PhoneFeatures.ALL,
) {
    /**
     * The identity sent over UDP. Capabilities follow over TLS, and leaving them out keeps the datagram
     * under one MTU: macOS refuses to send a broadcast that would need fragmenting.
     */
    fun announcement(): NetworkPacket = copy(incomingCapabilities = emptySet(), outgoingCapabilities = emptySet(), features = PhoneFeatures.ALL).toPacket()

    fun toPacket(): NetworkPacket = NetworkPacket(
        type = PacketType.IDENTITY,
        body = NetworkPacket.body(
            "deviceId" to deviceId,
            "deviceName" to deviceName,
            "deviceType" to deviceType.wire,
            "protocolVersion" to protocolVersion,
            "incomingCapabilities" to incomingCapabilities.sorted(),
            "outgoingCapabilities" to outgoingCapabilities.sorted(),
            "tcpPort" to tcpPort,
            PhoneFeatures.BODY_KEY to features.wire(),
        ),
    )

    companion object {
        private val ID_PATTERN = Regex("^[a-zA-Z0-9_]{32,38}$")
        private val NAME_FORBIDDEN = Regex("[\"',;:.!?()\\[\\]<>]")

        fun newDeviceId(): String = UUID.randomUUID().toString().replace("-", "")

        fun isValidDeviceId(id: String): Boolean = ID_PATTERN.matches(id)

        /** Protocol v8 names are 1 to 32 characters with no punctuation from a fixed list. */
        fun sanitizeName(name: String): String {
            val cleaned = NAME_FORBIDDEN.replace(name, "").trim().take(32)
            return cleaned.ifEmpty { "Android" }
        }

        fun fromPacket(packet: NetworkPacket): DeviceIdentity {
            if (packet.type != PacketType.IDENTITY) throw ProtocolException("not an identity packet: ${packet.type}")
            val id = packet.string("deviceId") ?: throw ProtocolException("missing deviceId")
            if (!isValidDeviceId(id)) throw ProtocolException("invalid deviceId")
            return DeviceIdentity(
                deviceId = id,
                deviceName = sanitizeName(packet.string("deviceName") ?: ""),
                deviceType = DeviceType.fromWire(packet.string("deviceType")),
                protocolVersion = packet.int("protocolVersion") ?: 0,
                incomingCapabilities = packet.stringList("incomingCapabilities").toSet(),
                outgoingCapabilities = packet.stringList("outgoingCapabilities").toSet(),
                tcpPort = packet.int("tcpPort"),
                features = PhoneFeatures.fromBody(packet),
            )
        }
    }
}
