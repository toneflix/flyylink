import XCTest
@testable import FlyyLinkKit

final class DeviceCertificateTests: XCTestCase {
    func testCommonNameIsDeviceId() throws {
        let id = DeviceIdentity.newDeviceId()

        let certificate = try DeviceCertificate.generate(deviceId: id)

        XCTAssertEqual(certificate.deviceId, id)
    }

    func testSurvivesStorage() throws {
        let certificate = try DeviceCertificate.generate(deviceId: DeviceIdentity.newDeviceId())

        let loaded = try DeviceCertificate(certificateDER: certificate.certificateDER, privateKeyPEM: certificate.privateKeyPEM)

        XCTAssertEqual(loaded.publicKeyDER, certificate.publicKeyDER)
    }

    func testVerificationKeyIsTheSameOnBothSides() throws {
        let a = try DeviceCertificate.generate(deviceId: DeviceIdentity.newDeviceId()).publicKeyDER
        let b = try DeviceCertificate.generate(deviceId: DeviceIdentity.newDeviceId()).publicKeyDER

        let code = VerificationKey.compute(a, b, timestamp: 1_700_000_000)

        XCTAssertEqual(code, VerificationKey.compute(b, a, timestamp: 1_700_000_000))
        XCTAssertEqual(code.count, 8)
        XCTAssertNotEqual(code, VerificationKey.compute(a, b, timestamp: 1_700_000_001))
    }

    /// Same inputs as the Kotlin side must give the same code.
    func testVerificationKeyMatchesKotlin() {
        XCTAssertEqual(VerificationKey.compute([1, 2, 3], [0xFF], timestamp: 42), VerificationKey.compute([0xFF], [1, 2, 3], timestamp: 42))
        XCTAssertEqual(VerificationKey.compute([1, 2, 3], [0xFF], timestamp: 42), "89CE2D9C")
    }
}

/// Collects links and device events so tests can wait on them.
final class Recorder: DeviceDelegate {
    let links = Queue<DeviceLink>()
    let requests = Queue<String>()
    let packets = Queue<NetworkPacket>()
    let paired = Queue<PairState>()
    let disconnected = Queue<Bool>()

    func devicePairRequested(_ device: Device, code: String) { requests.put(code) }

    func device(_ device: Device, pairStateChanged state: PairState) {
        if state == .paired { paired.put(state) }
    }

    func device(_ device: Device, received packet: NetworkPacket) { packets.put(packet) }

    func deviceDisconnected(_ device: Device) { disconnected.put(true) }
}

final class Queue<T> {
    private var items: [T] = []
    private let condition = NSCondition()

    func put(_ item: T) {
        condition.lock()
        items.append(item)
        condition.signal()
        condition.unlock()
    }

    func take(timeout: TimeInterval = 10) -> T? {
        let deadline = Date().addingTimeInterval(timeout)
        condition.lock()
        defer { condition.unlock() }

        while items.isEmpty {
            if !condition.wait(until: deadline) { return nil }
        }

        return items.removeFirst()
    }
}

final class LanLinkTests: XCTestCase {
    private struct Peer {
        let identity: DeviceIdentity
        let certificate: DeviceCertificate
        let trust: InMemoryTrustStore
        let recorder: Recorder
        let connected: ConnectedSet
        let lan: LanLink

        init(
            name: String,
            type: DeviceType,
            ports: ClosedRange<UInt16>,
            udpPort: UInt16 = 41716,
            capabilities: Set<String> = Capabilities.mvp,
            features: PhoneFeatures = .all
        ) throws {
            let identity = DeviceIdentity(
                deviceId: DeviceIdentity.newDeviceId(),
                deviceName: name,
                deviceType: type,
                incomingCapabilities: capabilities,
                outgoingCapabilities: capabilities,
                features: features
            )
            let certificate = try DeviceCertificate.generate(deviceId: identity.deviceId)
            let trust = InMemoryTrustStore()
            let recorder = Recorder()
            let connected = ConnectedSet()

            self.identity = identity
            self.certificate = certificate
            self.trust = trust
            self.recorder = recorder
            self.connected = connected
            // High ports so the test does not clash with a real KDE Connect install.
            lan = try LanLink(
                identityProvider: { identity },
                certificate: certificate,
                trustStore: trust,
                udpPort: udpPort,
                tcpPorts: ports,
                isConnected: { connected.contains($0) },
                onLink: { recorder.links.put($0) }
            )
        }
    }

    private var mac: Peer!
    private var phone: Peer!
    private var extra: Peer?

    override func setUpWithError() throws {
        mac = try Peer(name: "Mac", type: .laptop, ports: 41716...41730)
        phone = try Peer(name: "Phone", type: .phone, ports: 41731...41764)
        try mac.lan.start()
    }

    override func tearDown() {
        mac.lan.stop()
        extra?.lan.stop()
        extra = nil
    }

    private func connect() throws -> (onPhone: DeviceLink, onMac: DeviceLink) {
        var announced = mac.identity
        announced.tcpPort = mac.lan.tcpPort
        phone.lan.connect(to: announced, host: "127.0.0.1")

        let onPhone = try XCTUnwrap(phone.recorder.links.take())
        let onMac = try XCTUnwrap(mac.recorder.links.take())

        return (onPhone, onMac)
    }

    func testConnectsToAPeerWhoseAnnouncementIsLargerThanTwoKilobytes() throws {
        let capabilities = Set((0..<120).map { "flyylink.capability.\($0)" })
        let big = try Peer(name: "Phone", type: .phone, ports: 41765...41780, udpPort: 41799, capabilities: capabilities)
        // Kept until tearDown, so its event loops are shut down from the test thread and not from one of their own.
        extra = big
        try big.lan.start()

        var announced = big.identity
        announced.tcpPort = big.lan.tcpPort
        let datagram = try announced.toPacket().serialize()
        XCTAssertGreaterThan(datagram.count, 4096)

        try sendDatagram(datagram, port: 41716)

        XCTAssertNotNil(mac.recorder.links.take())
        XCTAssertNotNil(big.recorder.links.take())
    }

    private func sendDatagram(_ data: Data, port: UInt16) throws {
        let fd = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
        guard fd >= 0 else { throw ProtocolError("no socket") }
        defer { close(fd) }

        var addr = sockaddr_in()
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = port.bigEndian
        inet_pton(AF_INET, "127.0.0.1", &addr.sin_addr)

        let sent = data.withUnsafeBytes { buf in
            withUnsafePointer(to: &addr) { ptr in
                ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                    sendto(fd, buf.baseAddress, buf.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
                }
            }
        }
        guard sent == data.count else { throw ProtocolError("send failed: \(errno)") }
    }

    func testBroadcastsToEveryNetworkAndTheLimitedAddress() {
        let addresses = LanLink.broadcastAddresses()

        XCTAssertEqual(addresses.first, "255.255.255.255")
        XCTAssertEqual(addresses.count, Set(addresses).count)
        XCTAssertFalse(addresses.contains { $0.hasPrefix("127.") })
    }

    func testLinkIsEncryptedAndCarriesIdentities() throws {
        let (onPhone, onMac) = try connect()

        XCTAssertEqual(onPhone.identity.deviceId, mac.identity.deviceId)
        XCTAssertEqual(onMac.identity.deviceId, phone.identity.deviceId)
        XCTAssertEqual(DeviceCertificate.commonName(ofDER: onPhone.certificateDER), mac.identity.deviceId)
    }

    func testPairingShowsTheSameCodeAndTrustsBothSides() throws {
        let (onPhone, onMac) = try connect()
        let phoneDevice = Device(link: onPhone, ownCertificate: phone.certificate, trustStore: phone.trust)
        let macDevice = Device(link: onMac, ownCertificate: mac.certificate, trustStore: mac.trust)
        phoneDevice.delegate = phone.recorder
        macDevice.delegate = mac.recorder
        phoneDevice.start()
        macDevice.start()

        phoneDevice.requestPair()
        let codeOnMac = mac.recorder.requests.take()

        XCTAssertEqual(codeOnMac, phoneDevice.verificationCode)

        macDevice.accept()

        XCTAssertEqual(phone.recorder.paired.take(), .paired)
        XCTAssertTrue(phone.trust.isTrusted(mac.identity.deviceId))
        XCTAssertTrue(mac.trust.isTrusted(phone.identity.deviceId))

        XCTAssertTrue(phoneDevice.send(NetworkPacket(type: PacketType.ping)))
        XCTAssertEqual(mac.recorder.packets.take()?.type, PacketType.ping)
    }

    func testFeaturesArriveWithTheIdentityAndWhenTheyChange() throws {
        let phoneHas = FeatureBox(.of(.clipboard))
        phone = try Peer(name: "Phone", type: .phone, ports: 41731...41764, features: phoneHas.value)
        phone.trust.trust(mac.identity.deviceId, certificate: mac.certificate.certificateDER)
        mac.trust.trust(phone.identity.deviceId, certificate: phone.certificate.certificateDER)
        let (onPhone, onMac) = try connect()
        let phoneDevice = Device(link: onPhone, ownCertificate: phone.certificate, trustStore: phone.trust, ownFeatures: { phoneHas.value })
        let macDevice = Device(link: onMac, ownCertificate: mac.certificate, trustStore: mac.trust)
        phoneDevice.delegate = phone.recorder
        macDevice.delegate = mac.recorder
        phoneDevice.start()
        macDevice.start()

        XCTAssertEqual(macDevice.features, .of(.clipboard))
        XCTAssertFalse(macDevice.send(PhoneLocation.request()))
        XCTAssertFalse(phoneDevice.send(PhoneLocation(latitude: 1, longitude: 2, accuracy: 3, time: 4).toPacket()))

        onMac.send(PhoneLocation.request())
        XCTAssertTrue(macDevice.send(Clipboard.packet("hi")))
        XCTAssertEqual(phone.recorder.packets.take()?.type, PacketType.clipboard)

        phoneHas.value = .of(.clipboard, .location)
        XCTAssertTrue(phoneDevice.send(phoneHas.value.toPacket()))
        XCTAssertEqual(mac.recorder.packets.take()?.type, PacketType.features)

        XCTAssertEqual(macDevice.features, phoneHas.value)
        XCTAssertTrue(macDevice.send(PhoneLocation.request()))
        XCTAssertEqual(phone.recorder.packets.take()?.type, PacketType.locationRequest)
    }

    func testFeaturesChosenBeforePairingArriveOncePaired() throws {
        phone = try Peer(name: "Phone", type: .phone, ports: 41731...41764, features: .of())
        let (onPhone, onMac) = try connect()
        let phoneHas = PhoneFeatures.of(.clipboard, .files)
        let phoneDevice = Device(link: onPhone, ownCertificate: phone.certificate, trustStore: phone.trust, ownFeatures: { phoneHas })
        let macDevice = Device(link: onMac, ownCertificate: mac.certificate, trustStore: mac.trust)
        phoneDevice.delegate = phone.recorder
        macDevice.delegate = mac.recorder
        phoneDevice.start()
        macDevice.start()
        XCTAssertEqual(macDevice.features, .of())

        phoneDevice.requestPair()
        _ = mac.recorder.requests.take()
        macDevice.accept()

        XCTAssertEqual(mac.recorder.packets.take()?.type, PacketType.features)
        XCTAssertEqual(macDevice.features, phoneHas)
    }

    func testChangedCertificateIsRejected() throws {
        let other = try DeviceCertificate.generate(deviceId: phone.identity.deviceId)
        mac.trust.trust(phone.identity.deviceId, certificate: other.certificateDER)

        var announced = mac.identity
        announced.tcpPort = mac.lan.tcpPort
        phone.lan.connect(to: announced, host: "127.0.0.1")

        XCTAssertNil(mac.recorder.links.take(timeout: 3))
    }

    func testBothSidesKeepTheSameLinkWhenTheyConnectAtOnce() throws {
        try phone.lan.start()
        defer { phone.lan.stop() }

        var macAnnounced = mac.identity
        macAnnounced.tcpPort = mac.lan.tcpPort
        phone.lan.connect(to: macAnnounced, host: "127.0.0.1")
        let phoneOwn = try XCTUnwrap(phone.recorder.links.take())
        let macFromPhone = try XCTUnwrap(mac.recorder.links.take())

        var phoneAnnounced = phone.identity
        phoneAnnounced.tcpPort = phone.lan.tcpPort
        mac.lan.connect(to: phoneAnnounced, host: "127.0.0.1")
        let macOwn = try XCTUnwrap(mac.recorder.links.take())
        let phoneFromMac = try XCTUnwrap(phone.recorder.links.take())

        // The phone sees its own link first, the Mac sees its own first.
        let phoneKeepsItsOwn = DeviceLink.keepsExisting(phoneOwn, over: phoneFromMac, ownDeviceId: phone.identity.deviceId)
        let macKeepsItsOwn = DeviceLink.keepsExisting(macOwn, over: macFromPhone, ownDeviceId: mac.identity.deviceId)

        XCTAssertNotEqual(phoneKeepsItsOwn, macKeepsItsOwn)
    }

    func testReconnectReplacesTheOldLink() throws {
        let (first, _) = try connect()
        let (second, _) = try connect()

        XCTAssertFalse(DeviceLink.keepsExisting(first, over: second, ownDeviceId: phone.identity.deviceId))
    }

    func testHeartbeatIsAnswered() throws {
        let (onPhone, onMac) = try connect()
        let macDevice = Device(link: onMac, ownCertificate: mac.certificate, trustStore: mac.trust)
        macDevice.start()
        let received = Queue<NetworkPacket>()
        onPhone.onPacket = { received.put($0) }

        onPhone.send(NetworkPacket(type: PacketType.heartbeat, body: ["pong": .bool(false)]))

        let answer = received.take()
        XCTAssertEqual(answer?.type, PacketType.heartbeat)
        XCTAssertEqual(answer?.bool("pong"), true)
        withExtendedLifetime(macDevice) {}
    }

    func testSilentLinkIsClosed() throws {
        let (onPhone, _) = try connect()
        let clock = Clock()
        let device = Device(link: onPhone, ownCertificate: phone.certificate, trustStore: phone.trust, clock: { clock.now })
        device.delegate = phone.recorder
        device.start()

        clock.now += Device.heartbeatTimeoutSeconds + 1
        device.tick()

        XCTAssertEqual(phone.recorder.disconnected.take(), true)
        XCTAssertTrue(onPhone.closeReason?.contains("heartbeat") == true)
    }

    func testIdleLinkStaysOpen() throws {
        let (onPhone, onMac) = try connect()

        Thread.sleep(forTimeInterval: 11)

        XCTAssertTrue(onPhone.isOpen)
        XCTAssertTrue(onMac.isOpen)
    }

    func testFileTravelsOverItsOwnTlsConnection() throws {
        let (onPhone, onMac) = try connect()
        phone.trust.trust(mac.identity.deviceId, certificate: onPhone.certificateDER)
        mac.trust.trust(phone.identity.deviceId, certificate: onMac.certificateDER)
        let phoneDevice = Device(link: onPhone, ownCertificate: phone.certificate, trustStore: phone.trust)
        let macDevice = Device(link: onMac, ownCertificate: mac.certificate, trustStore: mac.trust)
        macDevice.delegate = mac.recorder
        phoneDevice.start()
        macDevice.start()

        let bytes = Data((0..<300_000).map { UInt8($0 % 251) })
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let sourceURL = folder.appendingPathComponent("photo.jpg")
        let sinkURL = folder.appendingPathComponent("received.jpg")
        try bytes.write(to: sourceURL)
        FileManager.default.createFile(atPath: sinkURL.path, contents: nil)

        let sent = phoneDevice.sendWithPayload(
            Share.filePacket(name: "photo.jpg", size: Int64(bytes.count)),
            size: Int64(bytes.count),
            source: try FileHandle(forReadingFrom: sourceURL)
        )
        let packet = try XCTUnwrap(mac.recorder.packets.take())
        let sink = try FileHandle(forWritingTo: sinkURL)
        try macDevice.receivePayload(packet, into: sink).wait()
        try sink.close()

        XCTAssertTrue(sent)
        XCTAssertEqual(Share(packet: packet), .file(name: "photo.jpg", size: Int64(bytes.count)))
        XCTAssertEqual(try Data(contentsOf: sinkURL), bytes)
    }

    func testFlyyDropMovesOnlyTheAcceptedFileBetweenUnpairedDevices() throws {
        let (onPhone, onMac) = try connect()
        let phoneDrop = FlyyDrop()
        let macDrop = FlyyDrop()
        macDrop.receiving = .everyone
        let phoneDevice = Device(link: onPhone, ownCertificate: phone.certificate, trustStore: phone.trust, guests: phoneDrop)
        let macDevice = Device(link: onMac, ownCertificate: mac.certificate, trustStore: mac.trust, guests: macDrop)
        phoneDevice.delegate = phone.recorder
        macDevice.delegate = mac.recorder
        phoneDevice.start()
        macDevice.start()

        let bytes = Data("hello from the phone".utf8)
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let sourceURL = folder.appendingPathComponent("hello.txt")
        let sinkURL = folder.appendingPathComponent("received.txt")
        try bytes.write(to: sourceURL)
        FileManager.default.createFile(atPath: sinkURL.path, contents: nil)

        XCTAssertFalse(phoneDevice.send(NetworkPacket(type: PacketType.ping)))
        let item = FlyyDrop.Item(name: "hello.txt", size: Int64(bytes.count))
        XCTAssertTrue(phoneDevice.send(phoneDrop.request(to: mac.identity.deviceId, items: [item])))

        let request = try XCTUnwrap(mac.recorder.packets.take())
        guard case .ask(let offer)? = macDrop.onRequest(from: phone.identity.deviceId, request, isPaired: false) else { return XCTFail("not asked") }
        XCTAssertTrue(macDevice.send(try XCTUnwrap(macDrop.accept(offer.id))))
        XCTAssertEqual(phoneDrop.onResponse(from: mac.identity.deviceId, try XCTUnwrap(phone.recorder.packets.take()))?.accepted, true)

        let sent = phoneDevice.sendWithPayload(FlyyDrop.file(offer.id, item), size: item.size, source: try FileHandle(forReadingFrom: sourceURL))
        let packet = try XCTUnwrap(mac.recorder.packets.take())
        let sink = try FileHandle(forWritingTo: sinkURL)
        try macDevice.receivePayload(packet, into: sink).wait()
        try sink.close()

        XCTAssertTrue(sent)
        XCTAssertEqual(try Data(contentsOf: sinkURL), bytes)
        XCTAssertFalse(phoneDevice.sendWithPayload(FlyyDrop.file(offer.id, item), size: item.size, source: try FileHandle(forReadingFrom: sourceURL)))
        XCTAssertFalse(mac.trust.isTrusted(phone.identity.deviceId))
    }

    func testLiveStreamIsReadUntilTheSenderCloses() throws {
        let (onPhone, onMac) = try connect()
        phone.trust.trust(mac.identity.deviceId, certificate: onPhone.certificateDER)
        mac.trust.trust(phone.identity.deviceId, certificate: onMac.certificateDER)
        let phoneDevice = Device(link: onPhone, ownCertificate: phone.certificate, trustStore: phone.trust)
        let macDevice = Device(link: onMac, ownCertificate: mac.certificate, trustStore: mac.trust)
        macDevice.delegate = mac.recorder
        phoneDevice.start()
        macDevice.start()

        let frames = [
            ScreenFrame(data: Data([0, 0, 0, 1, 0x67, 0x42]), timeUs: 0, flags: ScreenFrame.config),
            ScreenFrame(data: Data((0..<70_000).map { UInt8($0 % 256) }), timeUs: 16_666, flags: ScreenFrame.key),
        ]
        let streamURL = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try frames.reduce(Data()) { $0 + $1.header() + $1.data }.write(to: streamURL)

        XCTAssertTrue(phoneDevice.sendWithPayload(Screen.started(width: 1080, height: 2400), size: Screen.stream, source: try FileHandle(forReadingFrom: streamURL)))
        let packet = try XCTUnwrap(mac.recorder.packets.take())
        let reader = ScreenFrameReader()
        var received: [ScreenFrame] = []
        try macDevice.receiveStream(packet) { received += try reader.feed($0) }.wait()

        XCTAssertEqual(Screen.info(packet), Screen.Info(width: 1080, height: 2400, running: true))
        XCTAssertEqual(received, frames)
    }

    func testPayloadStreamSendsPiecesAsTheyAreWritten() throws {
        let (onPhone, onMac) = try connect()
        phone.trust.trust(mac.identity.deviceId, certificate: onPhone.certificateDER)
        mac.trust.trust(phone.identity.deviceId, certificate: onMac.certificateDER)
        let senderDevice = Device(link: onPhone, ownCertificate: phone.certificate, trustStore: phone.trust)
        let viewerDevice = Device(link: onMac, ownCertificate: mac.certificate, trustStore: mac.trust)
        viewerDevice.delegate = mac.recorder
        senderDevice.start()
        viewerDevice.start()

        let frames = [
            ScreenFrame(data: Data([1, 2, 3]), timeUs: 0, flags: ScreenFrame.key),
            ScreenFrame(data: Data((0..<70_000).map { UInt8($0 % 256) }), timeUs: 66_666, flags: ScreenFrame.key),
        ]
        let stream = PayloadStream()
        let opened = expectation(description: "opened")
        let closed = expectation(description: "closed")
        stream.onOpen = { opened.fulfill() }
        stream.onClose = { error in
            XCTAssertNil(error)
            closed.fulfill()
        }

        XCTAssertTrue(senderDevice.sendStream(Screen.started(width: 1440, height: 900, codec: Screen.jpeg), stream: stream))
        let packet = try XCTUnwrap(mac.recorder.packets.take())
        let reader = ScreenFrameReader()
        var received: [ScreenFrame] = []
        let reading = try viewerDevice.receiveStream(packet) { received += try reader.feed($0) }

        wait(for: [opened], timeout: 10)
        XCTAssertTrue(stream.write(frames[0].header() + frames[0].data))
        while !stream.write(frames[1].header() + frames[1].data) { usleep(1000) }
        stream.close()
        try reading.wait()
        wait(for: [closed], timeout: 10)

        XCTAssertEqual(Screen.info(packet)?.codec, Screen.jpeg)
        XCTAssertEqual(received, frames)
        XCTAssertFalse(stream.write(Data([4])))
    }

    func testAnnouncementConnectsToANewPeer() throws {
        var announced = mac.identity
        announced.tcpPort = mac.lan.tcpPort

        XCTAssertTrue(phone.lan.handleAnnouncement(announced, host: "127.0.0.1"))
        XCTAssertEqual(mac.recorder.links.take()?.identity.deviceId, phone.identity.deviceId)
    }

    func testAnnouncementKeepsTheExistingLink() throws {
        var announced = mac.identity
        announced.tcpPort = mac.lan.tcpPort
        phone.connected.insert(mac.identity.deviceId)

        XCTAssertFalse(phone.lan.handleAnnouncement(announced, host: "127.0.0.1"))
        XCTAssertNil(mac.recorder.links.take(timeout: 2))
    }
}

/// Device ids a test peer treats as already connected.
private final class ConnectedSet: @unchecked Sendable {
    private var ids: Set<String> = []
    private let lock = NSLock()

    func insert(_ id: String) {
        lock.withLock { _ = ids.insert(id) }
    }

    func contains(_ id: String) -> Bool {
        lock.withLock { ids.contains(id) }
    }
}

/// A clock tests can move forward.
private final class FeatureBox: @unchecked Sendable {
    var value: PhoneFeatures

    init(_ value: PhoneFeatures) {
        self.value = value
    }
}

private final class Clock: @unchecked Sendable {
    var now: Int64 = 1_000
}
