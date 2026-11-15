package com.toneflix.flyylink

import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.DeviceType
import com.toneflix.flyylink.protocol.Media
import com.toneflix.flyylink.protocol.MediaAction
import com.toneflix.flyylink.protocol.MediaPlayer
import com.toneflix.flyylink.protocol.MediaRequest
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.Plugin
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.concurrent.ConcurrentHashMap

/** What each paired TV is playing, for the phone's remote to show and control. */
object TvMediaPlugin : Plugin {
    override val incoming = setOf(PacketType.MPRIS)

    /** Every player on each TV, by device id and then player name. */
    private val players = ConcurrentHashMap<String, Map<String, MediaPlayer>>()

    private val _playing = MutableStateFlow<Map<String, MediaPlayer>>(emptyMap())

    /** The player that matters on each TV: the one playing, or else the first. */
    val playing: StateFlow<Map<String, MediaPlayer>> = _playing.asStateFlow()

    override fun onPaired(device: Device) {
        if (device.isTv) DeviceStore.send(device, MediaRequest.playerList().toPacket())
    }

    override fun onPacket(device: Device, packet: NetworkPacket) {
        if (!device.isTv) return

        Media.playerList(packet)?.let { names ->
            players[device.id] = players[device.id].orEmpty().filterKeys { it in names }
            names.forEach { DeviceStore.send(device, MediaRequest.nowPlaying(it).toPacket()) }
            publish()
            return
        }

        MediaPlayer.fromPacket(packet)?.let { player ->
            players[device.id] = players[device.id].orEmpty() + (player.name to player)
            publish()
        }
    }

    fun press(deviceId: String, action: MediaAction) {
        val player = _playing.value[deviceId] ?: return

        DeviceStore.find(deviceId)?.let { DeviceStore.send(it, MediaRequest.action(player.name, action).toPacket()) }
    }

    private fun publish() {
        _playing.value = players.mapNotNull { (id, byName) ->
            val pick = byName.values.firstOrNull { it.isPlaying } ?: byName.values.firstOrNull()
            pick?.let { id to it }
        }.toMap()
    }

    private val Device.isTv: Boolean get() = link.identity.deviceType == DeviceType.TV
}
