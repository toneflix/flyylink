package com.toneflix.flyylink.protocol

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit

class LanLinkTest {
    private class Peer(
        name: String,
        type: DeviceType,
        ports: IntRange,
        udpPort: Int = 41716,
        capabilities: Set<String> = Capabilities.mvp,
        features: PhoneFeatures = PhoneFeatures.ALL,
    ) {
        val identity = DeviceIdentity(
            DeviceIdentity.newDeviceId(), name, type,
            incomingCapabilities = capabilities, outgoingCapabilities = capabilities, features = features,
        )
        val certificate = DeviceCertificate.generate(identity.deviceId)
        val trust = InMemoryTrustStore()
        val links = LinkedBlockingQueue<DeviceLink>()
        val connected = ConcurrentHashMap.newKeySet<String>()

        // High ports so the test does not clash with a real KDE Connect install.
        val lan = LanLink(
            { identity }, certificate, trust, { links.put(it) },
            udpPort = udpPort, tcpPorts = ports, isConnected = { it in connected },
        )

        fun nextLink(): DeviceLink = links.poll(10, TimeUnit.SECONDS) ?: error("no link")
    }

    private lateinit var mac: Peer
    private lateinit var phone: Peer

    @Before fun setUp() {
        mac = Peer("Mac", DeviceType.LAPTOP, 41716..41730)
        phone = Peer("Phone", DeviceType.PHONE, 41731..41764)
        mac.lan.start()
    }

    @After fun tearDown() {
        mac.lan.stop()
    }

    private fun connect(): Pair<DeviceLink, DeviceLink> {
        phone.lan.connect(mac.identity.copy(tcpPort = mac.lan.tcpPort), InetAddress.getLoopbackAddress())

        return phone.nextLink() to mac.nextLink()
    }

    @Test fun connectsToAPeerWhoseAnnouncementIsLargerThanTwoKilobytes() {
        val capabilities = (0 until 120).map { "flyylink.capability.$it" }.toSet()
        val big = Peer("Phone", DeviceType.PHONE, 41765..41780, udpPort = 41799, capabilities = capabilities)
        big.lan.start()

        try {
            val datagram = big.identity.copy(tcpPort = big.lan.tcpPort).toPacket().serialize().toByteArray()
            assertTrue(datagram.size > 4096)

            DatagramSocket().use { it.send(DatagramPacket(datagram, datagram.size, InetAddress.getLoopbackAddress(), 41716)) }

            mac.nextLink()
        } finally {
            big.lan.stop()
        }
    }

    @Test fun broadcastsToEveryNetworkAndTheLimitedAddress() {
        val addresses = LanLink.broadcastAddresses()

        assertEquals(InetAddress.getByName("255.255.255.255"), addresses.first())
        assertEquals(addresses.size, addresses.toSet().size)
        assertTrue(addresses.none { it.isLoopbackAddress })
    }

    @Test fun linkIsEncryptedAndCarriesIdentities() {
        val (onPhone, onMac) = connect()

        assertEquals(mac.identity.deviceId, onPhone.identity.deviceId)
        assertEquals(phone.identity.deviceId, onMac.identity.deviceId)
        assertEquals(mac.identity.deviceId, DeviceCertificate.commonName(onPhone.certificate))
    }

    @Test fun pairingShowsTheSameCodeAndTrustsBothSides() {
        val (onPhone, onMac) = connect()
        val requests = LinkedBlockingQueue<String>()
        val pings = LinkedBlockingQueue<NetworkPacket>()
        val paired = LinkedBlockingQueue<PairState>()

        val phoneDevice = Device(onPhone, phone.certificate, phone.trust).apply {
            listener = object : Device.Listener {
                override fun onPairStateChanged(device: Device, state: PairState) {
                    if (state == PairState.PAIRED) paired.put(state)
                }
            }
            start()
        }
        val macDevice = Device(onMac, mac.certificate, mac.trust).apply {
            listener = object : Device.Listener {
                override fun onPairRequest(device: Device, code: String) = requests.put(code)

                override fun onPacket(device: Device, packet: NetworkPacket) = pings.put(packet)
            }
            start()
        }

        phoneDevice.requestPair()
        val codeOnMac = requests.poll(10, TimeUnit.SECONDS)

        assertEquals(phoneDevice.verificationCode, codeOnMac)

        macDevice.accept()

        assertEquals(PairState.PAIRED, paired.poll(10, TimeUnit.SECONDS))
        assertTrue(phone.trust.isTrusted(mac.identity.deviceId))
        assertTrue(mac.trust.isTrusted(phone.identity.deviceId))

        assertTrue(phoneDevice.send(NetworkPacket(PacketType.PING)))
        assertEquals(PacketType.PING, pings.poll(10, TimeUnit.SECONDS)?.type)
    }

    @Test fun unpairedDevicesCannotSend() {
        val (onPhone, onMac) = connect()
        val phoneDevice = Device(onPhone, phone.certificate, phone.trust)
        Device(onMac, mac.certificate, mac.trust).start()

        assertFalse(phoneDevice.send(NetworkPacket(PacketType.PING)))
    }

    @Test fun featuresArriveWithTheIdentityAndWhenTheyChange() {
        var phoneHas = PhoneFeatures.of(PhoneFeature.CLIPBOARD)
        phone = Peer("Phone", DeviceType.PHONE, 41731..41764, features = phoneHas)
        phone.trust.trust(mac.identity.deviceId, mac.certificate.encodedCertificate)
        mac.trust.trust(phone.identity.deviceId, phone.certificate.encodedCertificate)
        val (onPhone, onMac) = connect()
        val onPhoneReceived = LinkedBlockingQueue<NetworkPacket>()
        val onMacReceived = LinkedBlockingQueue<NetworkPacket>()

        val phoneDevice = Device(onPhone, phone.certificate, phone.trust, ownFeatures = { phoneHas }).apply {
            listener = object : Device.Listener {
                override fun onPacket(device: Device, packet: NetworkPacket) = onPhoneReceived.put(packet)
            }
            start()
        }
        val macDevice = Device(onMac, mac.certificate, mac.trust).apply {
            listener = object : Device.Listener {
                override fun onPacket(device: Device, packet: NetworkPacket) = onMacReceived.put(packet)
            }
            start()
        }

        assertEquals(phoneHas, macDevice.features)
        assertFalse(macDevice.send(PhoneLocation.request()))
        assertFalse(phoneDevice.send(PhoneLocation(1.0, 2.0, 3.0, 4).toPacket()))

        onMac.send(PhoneLocation.request())
        assertTrue(macDevice.send(Clipboard.packet("hi")))
        assertEquals(PacketType.CLIPBOARD, onPhoneReceived.poll(10, TimeUnit.SECONDS)?.type)

        phoneHas = PhoneFeatures.of(PhoneFeature.CLIPBOARD, PhoneFeature.LOCATION)
        assertTrue(phoneDevice.send(phoneHas.toPacket()))
        assertEquals(PacketType.FEATURES, onMacReceived.poll(10, TimeUnit.SECONDS)?.type)

        assertEquals(phoneHas, macDevice.features)
        assertTrue(macDevice.send(PhoneLocation.request()))
        assertEquals(PacketType.LOCATION_REQUEST, onPhoneReceived.poll(10, TimeUnit.SECONDS)?.type)
    }

    @Test fun featuresChosenBeforePairingArriveOncePaired() {
        phone = Peer("Phone", DeviceType.PHONE, 41731..41764, features = PhoneFeatures.of())
        val (onPhone, onMac) = connect()
        val phoneHas = PhoneFeatures.of(PhoneFeature.CLIPBOARD, PhoneFeature.FILES)
        val requests = LinkedBlockingQueue<String>()
        val changes = LinkedBlockingQueue<NetworkPacket>()

        val phoneDevice = Device(onPhone, phone.certificate, phone.trust, ownFeatures = { phoneHas }).apply { start() }
        val macDevice = Device(onMac, mac.certificate, mac.trust).apply {
            listener = object : Device.Listener {
                override fun onPairRequest(device: Device, code: String) = requests.put(code)

                override fun onPacket(device: Device, packet: NetworkPacket) = changes.put(packet)
            }
            start()
        }
        assertEquals(PhoneFeatures.of(), macDevice.features)

        phoneDevice.requestPair()
        requests.poll(10, TimeUnit.SECONDS)
        macDevice.accept()

        assertEquals(PacketType.FEATURES, changes.poll(10, TimeUnit.SECONDS)?.type)
        assertEquals(phoneHas, macDevice.features)
    }

    @Test fun flyyDropMovesOnlyTheAcceptedFileBetweenUnpairedDevices() {
        val (onPhone, onMac) = connect()
        val phoneDrop = FlyyDrop()
        val macDrop = FlyyDrop().apply { receiving = FlyyDrop.Receiving.EVERYONE }
        val onMacPackets = LinkedBlockingQueue<NetworkPacket>()
        val onPhonePackets = LinkedBlockingQueue<NetworkPacket>()
        val bytes = "hello from the phone".toByteArray()

        val phoneDevice = Device(onPhone, phone.certificate, phone.trust, guests = phoneDrop).apply {
            listener = object : Device.Listener {
                override fun onPacket(device: Device, packet: NetworkPacket) = onPhonePackets.put(packet)
            }
            start()
        }
        val macDevice = Device(onMac, mac.certificate, mac.trust, guests = macDrop).apply {
            listener = object : Device.Listener {
                override fun onPacket(device: Device, packet: NetworkPacket) = onMacPackets.put(packet)
            }
            start()
        }

        assertFalse(phoneDevice.send(NetworkPacket(PacketType.PING)))
        val item = FlyyDrop.Item("hello.txt", bytes.size.toLong())
        assertTrue(phoneDevice.send(phoneDrop.request(mac.identity.deviceId, listOf(item))))

        val request = onMacPackets.poll(10, TimeUnit.SECONDS)!!
        val offer = (macDrop.onRequest(phone.identity.deviceId, request, isPaired = false) as FlyyDrop.Incoming.Ask).offer
        assertTrue(macDevice.send(macDrop.accept(offer.id)!!))
        assertTrue(phoneDrop.onResponse(mac.identity.deviceId, onPhonePackets.poll(10, TimeUnit.SECONDS)!!)!!.accepted)

        assertTrue(phoneDevice.sendWithPayload(FlyyDrop.file(offer.id, item), item.size, { bytes.inputStream() }))
        val file = onMacPackets.poll(10, TimeUnit.SECONDS)!!
        val received = java.io.ByteArrayOutputStream()
        macDevice.receivePayload(file, received)

        assertEquals("hello from the phone", received.toString())
        assertFalse(phoneDevice.sendWithPayload(FlyyDrop.file(offer.id, item), item.size, { bytes.inputStream() }))
        assertFalse(mac.trust.isTrusted(phone.identity.deviceId))
    }

    @Test fun changedCertificateIsRejected() {
        mac.trust.trust(phone.identity.deviceId, DeviceCertificate.generate(phone.identity.deviceId).encodedCertificate)

        runCatching { phone.lan.connect(mac.identity.copy(tcpPort = mac.lan.tcpPort), InetAddress.getLoopbackAddress()) }

        assertEquals(null, mac.links.poll(3, TimeUnit.SECONDS))
    }

    @Test fun announcementConnectsToANewPeer() {
        val announced = mac.identity.copy(tcpPort = mac.lan.tcpPort)

        assertTrue(phone.lan.handleAnnouncement(announced, InetAddress.getLoopbackAddress()))
        assertEquals(phone.identity.deviceId, mac.nextLink().identity.deviceId)
    }

    @Test fun announcementKeepsTheExistingLink() {
        val announced = mac.identity.copy(tcpPort = mac.lan.tcpPort)
        phone.connected += mac.identity.deviceId

        assertFalse(phone.lan.handleAnnouncement(announced, InetAddress.getLoopbackAddress()))
        assertEquals(null, mac.links.poll(2, TimeUnit.SECONDS))
    }

    @Test fun bothSidesKeepTheSameLinkWhenTheyConnectAtOnce() {
        phone.lan.start()
        try {
            phone.lan.connect(mac.identity.copy(tcpPort = mac.lan.tcpPort), InetAddress.getLoopbackAddress())
            val phoneOpened = phone.nextLink() to mac.nextLink()
            mac.lan.connect(phone.identity.copy(tcpPort = phone.lan.tcpPort), InetAddress.getLoopbackAddress())
            val macOpened = mac.nextLink() to phone.nextLink()

            // The phone sees its own link first, the Mac sees its own first.
            val phoneKeepsItsOwn = DeviceLink.keepsExisting(phoneOpened.first, macOpened.second, phone.identity.deviceId)
            val macKeepsItsOwn = DeviceLink.keepsExisting(macOpened.first, phoneOpened.second, mac.identity.deviceId)

            assertTrue(phoneKeepsItsOwn != macKeepsItsOwn)
        } finally {
            phone.lan.stop()
        }
    }

    @Test fun aReconnectReplacesTheOldLink() {
        val (first, _) = connect()
        val (second, _) = connect()

        assertFalse(DeviceLink.keepsExisting(first, second, phone.identity.deviceId))
    }

    @Test fun heartbeatIsAnswered() {
        val (onPhone, onMac) = connect()
        val received = LinkedBlockingQueue<NetworkPacket>()
        Device(onMac, mac.certificate, mac.trust).start()
        onPhone.onPacket = { received.put(it) }
        onPhone.startReading()

        onPhone.send(NetworkPacket(PacketType.HEARTBEAT, NetworkPacket.body("pong" to false)))

        val answer = received.poll(10, TimeUnit.SECONDS)
        assertEquals(PacketType.HEARTBEAT, answer?.type)
        assertEquals(true, answer?.bool("pong"))
    }

    @Test fun silentLinkIsClosed() {
        val (onPhone, _) = connect()
        var now = 1_000L
        val closed = LinkedBlockingQueue<Boolean>()
        val device = Device(onPhone, phone.certificate, phone.trust, clock = { now }).apply {
            listener = object : Device.Listener {
                override fun onDisconnected(device: Device) = closed.put(true)
            }
            start()
        }

        now += Device.HEARTBEAT_TIMEOUT_SECONDS + 1
        device.tick()

        assertEquals(true, closed.poll(10, TimeUnit.SECONDS))
        assertTrue(onPhone.closeReason.orEmpty().contains("heartbeat"))
    }

    @Test fun idleLinkStaysOpen() {
        val (onPhone, onMac) = connect()
        onPhone.startReading()
        onMac.startReading()

        Thread.sleep(11_000)

        assertTrue(onPhone.isOpen)
        assertTrue(onMac.isOpen)
    }

    @Test fun fileTravelsOverItsOwnTlsConnection() {
        val (onPhone, onMac) = connect()
        phone.trust.trust(mac.identity.deviceId, onPhone.certificate.encoded)
        mac.trust.trust(phone.identity.deviceId, onMac.certificate.encoded)
        val shares = LinkedBlockingQueue<NetworkPacket>()
        val phoneDevice = Device(onPhone, phone.certificate, phone.trust).apply { start() }
        val macDevice = Device(onMac, mac.certificate, mac.trust).apply {
            listener = object : Device.Listener {
                override fun onPacket(device: Device, packet: NetworkPacket) = shares.put(packet)
            }
            start()
        }
        val bytes = ByteArray(300_000) { (it % 251).toByte() }

        phoneDevice.sendWithPayload(Share.file("photo.jpg", bytes.size.toLong()), bytes.size.toLong(), { bytes.inputStream() })
        val packet = shares.poll(10, TimeUnit.SECONDS) ?: error("no share")
        val received = java.io.ByteArrayOutputStream()
        macDevice.receivePayload(packet, received)

        assertEquals(Share.File("photo.jpg", bytes.size.toLong()), Share.fromPacket(packet))
        assertTrue(bytes.contentEquals(received.toByteArray()))
    }

    @Test fun payloadRefusesAnotherCertificate() {
        val (onPhone, onMac) = connect()
        val stranger = DeviceCertificate.generate(DeviceIdentity.newDeviceId()).certificate
        val sender = PayloadTransfer(phone.certificate)

        val port = sender.offer(stranger, { ByteArray(10).inputStream() })
        val result = runCatching {
            PayloadTransfer(mac.certificate).receive(InetAddress.getLoopbackAddress(), port, 10, onMac.certificate, java.io.ByteArrayOutputStream())
        }

        assertTrue(result.isFailure)
        onPhone.close()
    }

    @Test fun liveStreamIsReadUntilTheSenderCloses() {
        val (onPhone, onMac) = connect()
        val frames = listOf(
            ScreenFrame(byteArrayOf(0, 0, 0, 1, 0x67, 0x42), 0, ScreenFrame.CONFIG),
            ScreenFrame(ByteArray(70_000) { it.toByte() }, 16_666, ScreenFrame.KEY),
        )
        val stream = frames.fold(ByteArray(0)) { all, frame -> all + frame.header() + frame.data }

        val port = PayloadTransfer(phone.certificate).offer(onPhone.certificate, { stream.inputStream() })
        val received = java.io.ByteArrayOutputStream()
        PayloadTransfer(mac.certificate).receive(InetAddress.getLoopbackAddress(), port, Screen.STREAM, onMac.certificate, received)

        assertEquals(frames, ScreenFrame.Reader().feed(received.toByteArray()))
        onPhone.close()
    }
}
