package com.toneflix.flyylink

import android.content.Context
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
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors

/** What each paired TV or computer is playing, for the phone's remote and media controls to show and control. */
object RemoteMediaPlugin : Plugin {
    override val incoming = setOf(PacketType.MPRIS)

    /** Every player on each TV, by device id and then player name. */
    private val players = ConcurrentHashMap<String, Map<String, MediaPlayer>>()

    private val _playing = MutableStateFlow<Map<String, MediaPlayer>>(emptyMap())

    /** The player that matters on each TV: the one playing, or else the first. */
    val playing: StateFlow<Map<String, MediaPlayer>> = _playing.asStateFlow()

    private val _artwork = MutableStateFlow<Map<String, File>>(emptyMap())

    /** Album art the computers sent, by its address. */
    val artwork: StateFlow<Map<String, File>> = _artwork.asStateFlow()

    private val askedForArt = ConcurrentHashMap.newKeySet<String>()
    private var artFolder: File? = null

    /** Artwork arrives over its own connection, so it stays off the link's thread. */
    private val downloads = Executors.newSingleThreadExecutor()

    fun start(context: Context) {
        artFolder = File(context.cacheDir, "computer-art")
    }

    override fun onPaired(device: Device) {
        if (device.isRemote) DeviceStore.send(device, MediaRequest.playerList().toPacket())
    }

    override fun onPacket(device: Device, packet: NetworkPacket) {
        if (!device.isRemote) return

        Media.albumArtUrl(packet)?.let { url ->
            if (packet.payloadSize != null) downloads.execute { receiveArt(device, packet, url) }
            return
        }

        Media.playerList(packet)?.let { names ->
            players[device.id] = players[device.id].orEmpty().filterKeys { it in names }
            names.forEach { DeviceStore.send(device, MediaRequest.nowPlaying(it).toPacket()) }
            publish()
            return
        }

        MediaPlayer.fromPacket(packet)?.let { player ->
            players[device.id] = players[device.id].orEmpty() + (player.name to player)
            publish()
            askForArt(device, player)
        }
    }

    fun press(deviceId: String, action: MediaAction) {
        val player = _playing.value[deviceId] ?: return

        DeviceStore.find(deviceId)?.let { DeviceStore.send(it, MediaRequest.action(player.name, action).toPacket()) }
    }

    fun seek(deviceId: String, position: Long) {
        val player = _playing.value[deviceId] ?: return

        DeviceStore.find(deviceId)?.let { DeviceStore.send(it, MediaRequest.seek(player.name, position).toPacket()) }
    }

    private fun askForArt(device: Device, player: MediaPlayer) {
        val url = player.albumArtUrl ?: return
        if (artFolder == null || url in _artwork.value || !askedForArt.add(url)) return

        DeviceStore.send(device, MediaRequest.albumArt(player.name, url).toPacket())
    }

    private fun receiveArt(device: Device, packet: NetworkPacket, url: String) {
        val folder = artFolder ?: return
        folder.mkdirs()

        val target = File(folder, sha1(url))
        val partial = File(target.path + ".part")
        val received = runCatching { partial.outputStream().use { device.receivePayload(packet, it) } }.isSuccess
        if (!received || !partial.renameTo(target)) {
            partial.delete()
            askedForArt.remove(url)
            return
        }

        _artwork.value = _artwork.value + (url to target)
    }

    private fun sha1(text: String): String =
        MessageDigest.getInstance("SHA-1").digest(text.toByteArray()).joinToString("") { "%02x".format(it) }

    private fun publish() {
        _playing.value = players.mapNotNull { (id, byName) ->
            val pick = byName.values.firstOrNull { it.isPlaying } ?: byName.values.firstOrNull()
            pick?.let { id to it }
        }.toMap()
    }

    private val Device.isRemote: Boolean
        get() = link.identity.deviceType.let { it == DeviceType.TV || it.isComputer }
}
