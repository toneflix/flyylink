package com.toneflix.flyylink

import android.content.Context
import android.util.Log
import android.util.Base64
import com.toneflix.flyylink.protocol.Device
import com.toneflix.flyylink.protocol.DeviceCertificate
import com.toneflix.flyylink.protocol.DeviceFocus
import com.toneflix.flyylink.protocol.DeviceIdentity
import com.toneflix.flyylink.protocol.DeviceLink
import com.toneflix.flyylink.protocol.DeviceType
import com.toneflix.flyylink.protocol.NetworkPacket
import com.toneflix.flyylink.protocol.PacketType
import com.toneflix.flyylink.protocol.PairState
import com.toneflix.flyylink.protocol.PhoneFeatures
import com.toneflix.flyylink.protocol.PluginRouter
import com.toneflix.flyylink.protocol.TrustStore
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.util.concurrent.Executors

/** What the UI shows for one connected device. */
data class DeviceState(
    val id: String,
    val name: String,
    val type: DeviceType,
    val address: String,
    val pairState: PairState,
    val code: String?,
    /** Runs FlyyLink with FlyyDrop, so files can go to it without pairing. */
    val canFlyyDrop: Boolean = false,
    /** False for another phone, which is reachable only through FlyyDrop. */
    val canPair: Boolean = true,
)

/** This phone's identity and certificate, trusted devices, and the devices connected now. */
object DeviceStore {
    private const val TAG = "FlyyLinkLink"
    private const val PREFS = "flyylink"
    private const val TRUST_PREFS = "flyylink_trusted"
    private const val KEY_ID = "device_id"
    private const val KEY_FOCUS = "focused_device"
    private const val KEY_SWITCHER = "switcher_style"

    private val devices = linkedMapOf<String, Device>()
    private var context: Context? = null

    private var plugins = PluginRouter(AppForm.PHONE.plugins)

    /** Network writes must stay off the main thread, so all plugin sends go through here. */
    private val sender = Executors.newSingleThreadExecutor()

    private val _states = MutableStateFlow<List<DeviceState>>(emptyList())
    val states: StateFlow<List<DeviceState>> = _states.asStateFlow()

    private val _pairRequest = MutableStateFlow<DeviceState?>(null)

    /** A device waiting for the user to accept or reject pairing. */
    val pairRequest: StateFlow<DeviceState?> = _pairRequest.asStateFlow()

    private val _focusId = MutableStateFlow<String?>(null)

    /** The Mac the user picked to show when several are paired. Every paired Mac stays connected. */
    val focusId: StateFlow<String?> = _focusId.asStateFlow()

    private val _switcherStyle = MutableStateFlow(SwitcherStyle.CAROUSEL)
    val switcherStyle: StateFlow<SwitcherStyle> = _switcherStyle.asStateFlow()

    val listener = object : Device.Listener {
        override fun onPairRequest(device: Device, code: String) {
            _pairRequest.value = device.toState()
            publish()
            context?.let { MainActivity.bringToFront(it) }
        }

        override fun onPairStateChanged(device: Device, state: PairState) {
            if (state != PairState.REQUESTED_BY_PEER && _pairRequest.value?.id == device.id) {
                _pairRequest.value = null
            }
            if (state == PairState.PAIRED) plugins.onPaired(device)
            publish()
        }

        override fun onPacket(device: Device, packet: NetworkPacket) {
            plugins.dispatch(device, packet)
        }

        override fun onDisconnected(device: Device) = remove(device)
    }

    fun identity(context: Context): DeviceIdentity {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val id = prefs.getString(KEY_ID, null) ?: DeviceIdentity.newDeviceId().also {
            prefs.edit().putString(KEY_ID, it).apply()
        }

        val form = AppForm.of(context)

        return DeviceIdentity(
            deviceId = id,
            deviceName = form.deviceName(context),
            deviceType = form.deviceType,
            incomingCapabilities = form.capabilities,
            outgoingCapabilities = form.capabilities,
            features = if (form == AppForm.PHONE) Features.current.value else PhoneFeatures.ALL,
        )
    }

    fun restorePreferences(context: Context) {
        this.context = context.applicationContext
        plugins = PluginRouter(AppForm.of(context).plugins)
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        _focusId.value = prefs.getString(KEY_FOCUS, null)
        _switcherStyle.value = prefs.getString(KEY_SWITCHER, null)
            ?.let { name -> SwitcherStyle.entries.firstOrNull { it.name == name } }
            ?: SwitcherStyle.CAROUSEL
    }

    fun useSwitcher(context: Context, style: SwitcherStyle) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(KEY_SWITCHER, style.name).apply()
        _switcherStyle.value = style
    }

    fun focus(context: Context, id: String) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(KEY_FOCUS, id).apply()
        _focusId.value = id
    }

    /** The paired Mac the home screen and its actions are about. */
    fun focused(states: List<DeviceState>, focusId: String?): DeviceState? =
        DeviceFocus(focusId).pick(states.filter { it.pairState == PairState.PAIRED }, { it.id }, { it.name })

    /** Loads the certificate from app storage, creating it on first launch. */
    fun certificate(context: Context): DeviceCertificate {
        val certFile = File(context.filesDir, "device.crt")
        val keyFile = File(context.filesDir, "device.key")

        val deviceId = identity(context).deviceId

        // Reuse the stored certificate only if it belongs to this device id. Peers refuse
        // a certificate whose common name differs from the id we announce.
        if (certFile.exists() && keyFile.exists()) {
            val stored = runCatching { DeviceCertificate.load(certFile.readBytes(), keyFile.readBytes()) }.getOrNull()
            if (stored != null && stored.deviceId == deviceId) return stored
        }

        val certificate = DeviceCertificate.generate(deviceId)
        certFile.writeBytes(certificate.encodedCertificate)
        keyFile.writeBytes(certificate.encodedPrivateKey)

        return certificate
    }

    fun trustStore(context: Context): TrustStore = PreferencesTrustStore(context.applicationContext)

    @Synchronized fun add(device: Device, ownDeviceId: String) {
        val existing = devices[device.id]
        if (existing != null && DeviceLink.keepsExisting(existing.link, device.link, ownDeviceId)) {
            device.link.close("both sides connected at once; kept the other link")
            return
        }

        existing?.link?.close("replaced by a newer link")
        devices[device.id] = device
        Log.i(TAG, "Connected to ${device.name} (${if (device.link.isOutgoing) "we" else "they"} connected)")
        device.listener = listener
        device.start()
        if (device.isPaired) plugins.onPaired(device)
        publish()
    }

    fun send(device: Device, packet: NetworkPacket) {
        sender.execute { runCatching { device.send(packet) } }
    }

    /** Sends [packet] with [payload] attached. Falls back to the bare packet if no payload port is free. */
    fun sendWithPayload(device: Device, packet: NetworkPacket, payload: ByteArray, onDone: (Throwable?) -> Unit = {}) {
        offer(device, packet, payload.size.toLong(), open = { payload.inputStream() }, onDone = { error ->
            if (error is PayloadNotOffered) runCatching { device.send(packet) }
            onDone(error)
        })
    }

    /** Offers a payload of [size] bytes (or a live stream) off the main thread; [onDone] gets [PayloadNotOffered] if it never went out. */
    fun offer(device: Device, packet: NetworkPacket, size: Long, open: () -> InputStream, onDone: (Throwable?) -> Unit = {}) {
        sender.execute {
            val offered = runCatching { device.sendWithPayload(packet, size, open, onDone) }.getOrDefault(false)

            if (!offered) onDone(PayloadNotOffered())
        }
    }

    class PayloadNotOffered : IOException("payload not offered")

    fun pairedDevices(): List<Device> = synchronized(this) { devices.values.filter { it.isPaired } }

    fun sendToPaired(packet: NetworkPacket) {
        val targets = synchronized(this) { devices.values.filter { it.isPaired } }

        targets.forEach { send(it, packet) }
    }

    @Synchronized fun remove(device: Device) {
        Log.i(TAG, "Disconnected from ${device.name}: ${device.link.closeReason}")
        if (devices[device.id] === device) devices.remove(device.id)
        if (_pairRequest.value?.id == device.id) _pairRequest.value = null
        publish()
    }

    @Synchronized fun find(id: String): Device? = devices[id]

    @Synchronized fun tickAll() = devices.values.forEach { it.tick() }

    @Synchronized fun clear() {
        devices.values.toList().forEach { it.link.close() }
        devices.clear()
        _pairRequest.value = null
        publish()
    }

    @Synchronized private fun publish() {
        _states.value = devices.values.map { it.toState() }
    }

    private fun Device.toState() = DeviceState(
        id = id,
        name = name,
        type = link.identity.deviceType,
        address = link.address.hostAddress ?: "",
        pairState = pairState,
        code = if (pairState == PairState.REQUESTED || pairState == PairState.REQUESTED_BY_PEER) verificationCode else null,
        canFlyyDrop = PacketType.FLYYDROP_REQUEST in link.identity.incomingCapabilities,
        canPair = canPair,
    )

    private class PreferencesTrustStore(context: Context) : TrustStore {
        private val prefs = context.getSharedPreferences(TRUST_PREFS, Context.MODE_PRIVATE)

        override fun certificate(deviceId: String): ByteArray? =
            prefs.getString(deviceId, null)?.let { Base64.decode(it, Base64.NO_WRAP) }

        override fun trust(deviceId: String, encodedCertificate: ByteArray) {
            prefs.edit().putString(deviceId, Base64.encodeToString(encodedCertificate, Base64.NO_WRAP)).apply()
        }

        override fun forget(deviceId: String) {
            prefs.edit().remove(deviceId).apply()
        }
    }
}
