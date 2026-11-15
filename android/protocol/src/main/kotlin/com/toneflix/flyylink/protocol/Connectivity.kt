package com.toneflix.flyylink.protocol

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.intOrNull

/** One SIM's mobile signal: [networkType] as KDE Connect names it ("LTE", "5G", "UMTS"...) and [strength] from 0 to 4 bars. */
data class CellSignal(val networkType: String, val strength: Int) {
    /** The short name a status bar shows, such as "4G", or "" when unknown. */
    val generation: String
        get() = when (networkType) {
            NR -> "5G"
            LTE -> "4G"
            HSPA -> "H"
            UMTS, CDMA2000 -> "3G"
            EDGE -> "E"
            GPRS, GSM, CDMA, IDEN -> "2G"
            else -> ""
        }

    companion object {
        const val MAX_STRENGTH = 4

        const val NR = "5G"
        const val LTE = "LTE"
        const val HSPA = "HSPA"
        const val UMTS = "UMTS"
        const val CDMA2000 = "CDMA2000"
        const val EDGE = "EDGE"
        const val GPRS = "GPRS"
        const val GSM = "GSM"
        const val CDMA = "CDMA"
        const val IDEN = "iDEN"
        const val UNKNOWN = "Unknown"
    }
}

/** The phone's mobile signal for each SIM ("kdeconnect.connectivity_report"), keyed by subscription id. */
data class ConnectivityReport(val signals: Map<String, CellSignal>) {
    /** The SIM a computer shows: the one with the most bars. */
    val best: CellSignal? get() = signals.toSortedMap().values.maxByOrNull { it.strength }

    fun toPacket(): NetworkPacket {
        val strengths = buildJsonObject {
            signals.forEach { (id, signal) ->
                put(id, NetworkPacket.body("networkType" to signal.networkType, "signalStrength" to signal.strength))
            }
        }

        return NetworkPacket(PacketType.CONNECTIVITY_REPORT, NetworkPacket.body("signalStrengths" to strengths))
    }

    companion object {
        fun request(): NetworkPacket = NetworkPacket(PacketType.CONNECTIVITY_REPORT_REQUEST)

        fun fromPacket(packet: NetworkPacket): ConnectivityReport? {
            if (packet.type != PacketType.CONNECTIVITY_REPORT) return null

            val strengths = packet.body["signalStrengths"] as? JsonObject ?: return ConnectivityReport(emptyMap())
            val signals = strengths.mapNotNull { (id, value) ->
                val entry = value as? JsonObject ?: return@mapNotNull null
                val strength = (entry["signalStrength"] as? JsonPrimitive)?.intOrNull ?: return@mapNotNull null
                val type = (entry["networkType"] as? JsonPrimitive)?.takeIf { it.isString }?.content ?: CellSignal.UNKNOWN

                id to CellSignal(type, strength.coerceIn(0, CellSignal.MAX_STRENGTH))
            }

            return ConnectivityReport(signals.toMap())
        }
    }
}
