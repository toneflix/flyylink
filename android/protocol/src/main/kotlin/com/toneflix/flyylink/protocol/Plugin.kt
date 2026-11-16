package com.toneflix.flyylink.protocol

/** A feature that handles some packet types for paired devices. */
interface Plugin {
    /** Packet types this plugin accepts. */
    val incoming: Set<String>

    /** The device just became paired, or connected while already paired. */
    fun onPaired(device: Device) {}

    /** The device turned a feature on or off; see [PhoneFeatures]. */
    fun onFeaturesChanged(device: Device) {}

    fun onPacket(device: Device, packet: NetworkPacket)
}

/** Hands each packet to the plugins that accept its type. */
class PluginRouter(private val plugins: List<Plugin>) {
    val incomingCapabilities: Set<String> = plugins.flatMapTo(mutableSetOf()) { it.incoming }

    fun onPaired(device: Device) {
        plugins.forEach { it.onPaired(device) }
    }

    fun onFeaturesChanged(device: Device) {
        plugins.forEach { it.onFeaturesChanged(device) }
    }

    /** Returns false when no plugin accepts the packet. */
    fun dispatch(device: Device, packet: NetworkPacket): Boolean {
        val targets = plugins.filter { packet.type in it.incoming }
        targets.forEach { it.onPacket(device, packet) }

        return targets.isNotEmpty()
    }
}
