import Foundation
import NIOCore

public protocol DeviceDelegate: AnyObject {
    /// The peer asked to pair. Show `code` and call `accept()` or `reject()`.
    func devicePairRequested(_ device: Device, code: String)
    func device(_ device: Device, pairStateChanged state: PairState)
    func device(_ device: Device, received packet: NetworkPacket)
    func deviceDisconnected(_ device: Device)
}

public extension DeviceDelegate {
    func devicePairRequested(_ device: Device, code: String) {}
    func device(_ device: Device, pairStateChanged state: PairState) {}
    func device(_ device: Device, received packet: NetworkPacket) {}
    func deviceDisconnected(_ device: Device) {}
}

/// A connected peer: its encrypted link plus pairing. Packets from a device that
/// is not paired are dropped, except pairing packets and what `guests` lets through. Mirrors Device.kt.
/// With `canPair` false the peer stays unpaired, so only `guests` traffic gets through.
/// Packets of a feature that is off, in `ownFeatures` or the peer's `features`, go neither way.
public final class Device {
    public let link: DeviceLink
    public let canPair: Bool
    public weak var delegate: DeviceDelegate?

    private let ownCertificate: DeviceCertificate
    private let trustStore: TrustStore
    private let clock: () -> Int64
    private let guests: GuestAccess?
    private let pairing: PairingStateMachine
    private let ownFeatures: () -> PhoneFeatures
    private var peerFeatures: PhoneFeatures
    private let lock = NSLock()
    private var lastReceived: Int64
    private lazy var payloads = try? PayloadTransfer(certificate: ownCertificate)

    /// A link with no packets for this long is treated as dead.
    public static let heartbeatTimeoutSeconds: Int64 = 30

    public init(
        link: DeviceLink,
        ownCertificate: DeviceCertificate,
        trustStore: TrustStore,
        clock: @escaping () -> Int64 = { Int64(Date().timeIntervalSince1970) },
        guests: GuestAccess? = nil,
        canPair: Bool = true,
        ownFeatures: @escaping () -> PhoneFeatures = { .all }
    ) {
        self.link = link
        self.canPair = canPair
        self.guests = guests
        self.ownCertificate = ownCertificate
        self.trustStore = trustStore
        self.clock = clock
        self.pairing = PairingStateMachine(
            state: trustStore.isTrusted(link.identity.deviceId) ? .paired : .notPaired,
            allowed: canPair
        )
        self.lastReceived = clock()
        self.ownFeatures = ownFeatures
        self.peerFeatures = link.identity.features

        if !canPair { trustStore.forget(link.identity.deviceId) }
    }

    public var id: String { link.identity.deviceId }

    public var name: String { link.identity.deviceName }

    public var pairState: PairState { lock.withLock { pairing.state } }

    /// The features the peer has on: from its identity, then from each `PacketType.features` it sends.
    public var features: PhoneFeatures { lock.withLock { peerFeatures } }

    public var isPaired: Bool { pairState == .paired }

    /// The code to show on screen while a pair request is open.
    public var verificationCode: String {
        let timestamp = lock.withLock { pairing.requestTimestamp }

        return VerificationKey.compute(ownCertificate.publicKeyDER, link.publicKeyDER, timestamp: timestamp)
    }

    public func start() {
        link.onPacket = { [weak self] packet in self?.handle(packet) }
        link.onClosed = { [weak self] in
            guard let self else { return }
            self.delegate?.deviceDisconnected(self)
        }
    }

    @discardableResult
    public func send(_ packet: NetworkPacket) -> Bool {
        guard maySend(packet) else { return false }

        link.send(packet)

        return true
    }

    /// Sends `packet` with a payload that the peer downloads from us, such as a file.
    /// `onProgress` gets each piece's byte count as it goes out.
    /// Returns false when not allowed to send or the payload could not be offered. Call off the event loops.
    @discardableResult
    public func sendWithPayload(
        _ packet: NetworkPacket,
        size: Int64,
        source: FileHandle,
        onProgress: ((Int64) -> Void)? = nil,
        completion: @escaping (Error?) -> Void = { _ in }
    ) -> Bool {
        guard maySend(packet), let payloads = lock.withLock({ payloads }) else { return false }

        do {
            let port = try payloads.offer(
                peerCertificateDER: link.certificateDER,
                source: source,
                onProgress: onProgress,
                completion: completion
            )
            var announced = packet
            announced.payloadSize = size
            announced.payloadTransferInfo = ["port": .int(Int64(port))]
            link.send(announced)

            return true
        } catch {
            completion(error)
            return false
        }
    }

    /// Sends `packet` with a live `stream` as its payload, such as a shared screen.
    /// Returns false when not paired or the stream could not be offered. Call off the event loops.
    @discardableResult
    public func sendStream(_ packet: NetworkPacket, stream: PayloadStream) -> Bool {
        guard isPaired, let payloads = lock.withLock({ payloads }) else { return false }

        do {
            let port = try payloads.offer(peerCertificateDER: link.certificateDER, stream: stream)
            var announced = packet
            announced.payloadSize = Screen.stream
            announced.payloadTransferInfo = ["port": .int(Int64(port))]
            link.send(announced)

            return true
        } catch {
            stream.finished(error)
            return false
        }
    }

    /// Downloads the payload of a packet from this device into `sink`. `onProgress` gets each piece's byte count.
    public func receivePayload(
        _ packet: NetworkPacket,
        into sink: FileHandle,
        onProgress: ((Int64) -> Void)? = nil
    ) throws -> EventLoopFuture<Void> {
        guard let size = packet.payloadSize, let port = packet.payloadPort else { throw ProtocolError("packet has no payload") }
        guard let payloads = lock.withLock({ payloads }) else { throw ProtocolError("payloads unavailable") }

        return payloads.receive(
            host: link.host,
            port: port,
            size: size,
            peerCertificateDER: link.certificateDER,
            into: sink,
            onProgress: onProgress
        )
    }

    /// Reads the live stream a packet announces, such as the mirrored screen, until the device closes it.
    /// `onData` runs on a network thread. To end it early, send `Screen.stop()`.
    public func receiveStream(_ packet: NetworkPacket, onData: @escaping (Data) throws -> Void) throws -> EventLoopFuture<Void> {
        guard packet.payloadSize == Screen.stream, let port = packet.payloadPort else { throw ProtocolError("packet has no stream") }
        guard let payloads = lock.withLock({ payloads }) else { throw ProtocolError("payloads unavailable") }

        return payloads.stream(host: link.host, port: port, peerCertificateDER: link.certificateDER, onData: onData)
    }

    public func requestPair() { run { $0.requestPair(now: clock()) } }

    public func accept() { run { $0.accept() } }

    public func reject() { run { $0.reject() } }

    public func unpair() { run { $0.unpair() } }

    /// Call every few seconds: expires unanswered pair requests and checks the link is alive.
    public func tick() {
        run { $0.tick(now: clock()) }
        heartbeat()
    }

    /// Older peers and KDE Connect itself don't send heartbeats, so only expect them from ours.
    private var sendsHeartbeats: Bool {
        link.identity.outgoingCapabilities.contains(PacketType.heartbeat)
    }

    /// Pings the peer, and closes the link if nothing has arrived for a while.
    private func heartbeat() {
        guard sendsHeartbeats else { return }

        let silent = clock() - lock.withLock { lastReceived }
        if silent > Self.heartbeatTimeoutSeconds {
            link.close(reason: "no heartbeat for \(Self.heartbeatTimeoutSeconds) seconds")
            return
        }

        link.send(Self.heartbeatPacket(pong: false))
    }

    private func handle(_ packet: NetworkPacket) {
        let now = clock()
        lock.withLock { lastReceived = now }

        if packet.type == PacketType.heartbeat {
            // Answer right away: a sleeping phone may not run its own timer, but it wakes for this.
            if packet.bool("pong") != true { link.send(Self.heartbeatPacket(pong: true)) }
            return
        }

        if packet.type == PacketType.pair {
            run { $0.handle(packet, now: clock()) }
            return
        }

        if packet.type == PacketType.features, isPaired, let features = PhoneFeatures(packet: packet) {
            lock.withLock { peerFeatures = features }
        }

        guard ownFeatures().accepts(packet.type) else { return }

        if isPaired || guests?.admitsIncoming(from: id, packet) == true { delegate?.device(self, received: packet) }
    }

    private func maySend(_ packet: NetworkPacket) -> Bool {
        guard ownFeatures().sends(packet.type), features.accepts(packet.type) else { return false }

        return isPaired || guests?.admitsOutgoing(to: id, packet) == true
    }

    private func run(_ step: (PairingStateMachine) -> PairAction) {
        let (action, state) = lock.withLock { (step(pairing), pairing.state) }

        switch action {
        case .none:
            return
        case .askUser:
            delegate?.devicePairRequested(self, code: verificationCode)
        case .send(let packet):
            link.send(packet)
        case .paired, .unpaired:
            break
        }

        switch state {
        case .paired: trustStore.trust(id, certificate: link.certificateDER)
        case .notPaired: trustStore.forget(id)
        case .requested, .requestedByPeer: break
        }

        if state == .paired, action.becamePaired { sendFeatures() }
        delegate?.device(self, pairStateChanged: state)
    }

    /// The peer ignores feature changes until paired, so tell it what is on once pairing is done.
    private func sendFeatures() {
        let own = ownFeatures()
        if own.reported != nil { link.send(own.toPacket()) }
    }

    private static func heartbeatPacket(pong: Bool) -> NetworkPacket {
        NetworkPacket(type: PacketType.heartbeat, body: ["pong": .bool(pong)])
    }
}

private extension PairAction {
    /// Accepting sends the answer; being accepted arrives as `paired`.
    var becamePaired: Bool {
        switch self {
        case .paired, .send: return true
        case .none, .askUser, .unpaired: return false
        }
    }
}
