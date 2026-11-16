package com.toneflix.flyylink.protocol

import java.io.InputStream
import java.io.OutputStream

/**
 * A connected peer: its encrypted link plus pairing. Packets from a device that
 * is not paired are dropped, except pairing packets and what [guests] lets through.
 * With [canPair] false the peer stays unpaired, so only [guests] traffic gets through.
 * Packets of a feature that is off, in [ownFeatures] or the peer's [features], go neither way.
 */
class Device(
    val link: DeviceLink,
    private val ownCertificate: DeviceCertificate,
    private val trustStore: TrustStore,
    private val clock: () -> Long = { System.currentTimeMillis() / 1000 },
    private val guests: GuestAccess? = null,
    val canPair: Boolean = true,
    private val ownFeatures: () -> PhoneFeatures = { PhoneFeatures.ALL },
) {
    interface Listener {
        /** The peer asked to pair. Show [code] and call [accept] or [reject]. */
        fun onPairRequest(device: Device, code: String) {}

        fun onPairStateChanged(device: Device, state: PairState) {}

        fun onPacket(device: Device, packet: NetworkPacket) {}

        fun onDisconnected(device: Device) {}
    }

    val id: String get() = link.identity.deviceId

    val name: String get() = link.identity.deviceName

    var listener: Listener? = null

    /** The features the peer has on: from its identity, then from each [PacketType.FEATURES] it sends. */
    @Volatile var features: PhoneFeatures = link.identity.features
        private set

    private val payloads by lazy { PayloadTransfer(ownCertificate) }

    @Volatile private var lastReceived = clock()

    /** Older peers and KDE Connect itself don't send heartbeats, so only expect them from ours. */
    private val sendsHeartbeats: Boolean
        get() = PacketType.HEARTBEAT in link.identity.outgoingCapabilities

    private val pairing = PairingStateMachine(
        if (trustStore.isTrusted(id)) PairState.PAIRED else PairState.NOT_PAIRED,
        allowed = canPair,
    )

    init {
        if (!canPair) trustStore.forget(id)
    }

    val pairState: PairState get() = pairing.state

    val isPaired: Boolean get() = pairing.state == PairState.PAIRED

    /** The code to show on screen while a pair request is open. */
    val verificationCode: String
        get() = VerificationKey.compute(
            ownCertificate.publicKeyBytes,
            link.certificate.publicKey.encoded,
            pairing.requestTimestamp,
        )

    fun start() {
        link.onPacket = ::handle
        link.onClosed = { listener?.onDisconnected(this) }
        link.startReading()
    }

    fun send(packet: NetworkPacket): Boolean {
        if (!maySend(packet)) return false

        link.send(packet)

        return true
    }

    /**
     * Sends [packet] with a payload that the peer downloads from us, such as a file.
     * [open] is called once the peer connects. Returns false when not paired
     * and not let through by [guests].
     */
    fun sendWithPayload(
        packet: NetworkPacket,
        size: Long,
        open: () -> InputStream,
        onDone: (Throwable?) -> Unit = {},
    ): Boolean {
        if (!maySend(packet)) return false

        val port = payloads.offer(link.certificate, open, onDone)
        link.send(packet.copy(payloadSize = size, payloadTransferInfo = NetworkPacket.body("port" to port)))

        return true
    }

    /** Downloads the payload of a packet from this device into [sink]. Blocks until done. */
    fun receivePayload(packet: NetworkPacket, sink: OutputStream) {
        val size = packet.payloadSize ?: throw ProtocolException("packet has no payload")
        val port = packet.payloadPort ?: throw ProtocolException("packet has no payload port")

        payloads.receive(link.address, port, size, link.certificate, sink)
    }

    fun requestPair() = apply(pairing.requestPair(clock()))

    fun accept() = apply(pairing.accept())

    fun reject() = apply(pairing.reject())

    fun unpair() = apply(pairing.unpair())

    /** Call every few seconds: expires unanswered pair requests and checks the link is alive. */
    fun tick() {
        apply(pairing.tick(clock()))
        heartbeat()
    }

    /** Pings the peer, and closes the link if nothing has arrived for a while. */
    private fun heartbeat() {
        if (!sendsHeartbeats) return

        if (clock() - lastReceived > HEARTBEAT_TIMEOUT_SECONDS) {
            link.close("no heartbeat for $HEARTBEAT_TIMEOUT_SECONDS seconds")
            return
        }

        runCatching { link.send(heartbeatPacket(pong = false)) }.onFailure { link.close("send failed: $it") }
    }

    private fun handle(packet: NetworkPacket) {
        lastReceived = clock()

        if (packet.type == PacketType.HEARTBEAT) {
            // Answer right away: a sleeping phone may not run its own timer, but it wakes for this.
            if (packet.bool("pong") != true) runCatching { link.send(heartbeatPacket(pong = true)) }
            return
        }

        if (packet.type == PacketType.PAIR) {
            apply(pairing.onPacket(packet, clock()))
            return
        }

        if (packet.type == PacketType.FEATURES && isPaired) PhoneFeatures.fromPacket(packet)?.let { features = it }

        if (!ownFeatures().accepts(packet.type)) return

        if (isPaired || guests?.admitsIncoming(id, packet) == true) listener?.onPacket(this, packet)
    }

    private fun maySend(packet: NetworkPacket): Boolean {
        if (!ownFeatures().sends(packet.type) || !features.accepts(packet.type)) return false

        return isPaired || guests?.admitsOutgoing(id, packet) == true
    }

    private fun apply(action: PairAction) {
        when (action) {
            PairAction.None -> return
            PairAction.AskUser -> listener?.onPairRequest(this, verificationCode)
            is PairAction.Send -> link.send(action.packet)
            PairAction.Paired, PairAction.Unpaired -> Unit
        }

        syncTrust()
        listener?.onPairStateChanged(this, pairing.state)
    }

    private fun syncTrust() {
        when (pairing.state) {
            PairState.PAIRED -> trustStore.trust(id, link.certificate.encoded)
            PairState.NOT_PAIRED -> trustStore.forget(id)
            else -> Unit
        }
    }

    companion object {
        /** A link with no packets for this long is treated as dead. */
        const val HEARTBEAT_TIMEOUT_SECONDS = 30L

        private fun heartbeatPacket(pong: Boolean) = NetworkPacket(PacketType.HEARTBEAT, NetworkPacket.body("pong" to pong))
    }
}
