import Foundation
import FlyyLinkKit

/// A FlyyDrop someone nearby wants to send, waiting for the user to answer.
struct FlyyDropAsk: Identifiable, Equatable {
    let offer: FlyyDrop.Offer
    let deviceName: String
    let deviceType: DeviceType
    let isPaired: Bool

    var id: String { offer.id }

    /// "3 photos · 12 MB", "report.pdf · 2 MB" or the link itself.
    var summary: String {
        let items = offer.items
        guard !items.isEmpty else { return offer.text?.split(whereSeparator: \.isNewline).first.map(String.init) ?? "" }

        let images = items.allSatisfy { Self.imageExtensions.contains(($0.name as NSString).pathExtension.lowercased()) }
        let what = items.count == 1 ? items[0].safeName : "\(items.count) \(images ? "photos" : "files")"

        return "\(what) · \(Bytes.text(offer.totalSize))"
    }

    var isImages: Bool {
        !offer.items.isEmpty && offer.items.allSatisfy { Self.imageExtensions.contains(($0.name as NSString).pathExtension.lowercased()) }
    }

    private static let imageExtensions: Set<String> = ["jpg", "jpeg", "png", "gif", "webp", "heic", "heif"]
}

/// How a FlyyDrop to one device is going.
struct FlyyDropSend: Equatable {
    enum Stage { case waiting, sending, sent, declined, noAnswer, failed }

    let id: String
    var stage: Stage
    var progress: TransferProgress.Snapshot?
    var reason: FlyyDrop.Reason?

    var isFinished: Bool { stage != .waiting && stage != .sending }

    /// A short line for the device's row.
    var status: String {
        switch stage {
        case .waiting: return "Waiting for them to accept…"
        case .sending: return progress.map { "Sending · \(Int($0.fraction * 100))%" } ?? "Sending…"
        case .sent: return "Sent"
        case .noAnswer: return "They didn't answer"
        case .failed: return "It didn't finish. Please try again."
        case .declined:
            switch reason {
            case .unavailable: return "They're not taking FlyyDrops right now"
            case .busy: return "They're busy with another request"
            default: return "Declined"
            }
        }
    }
}

/// FlyyDrop on the Mac: asks nearby devices, paired or not, to take files or a link, and asks the user before
/// taking theirs. `drop` is the one authority on what an unpaired device may send or receive; accepted files
/// then move through `SharePlugin` like any other share.
final class FlyyDropPlugin: Plugin {
    let incoming: Set<String> = [PacketType.flyyDropRequest, PacketType.flyyDropResponse, PacketType.flyyDropCancel]

    let drop = FlyyDrop()

    /// Someone asked to send something. Called on any thread.
    var onAsk: ((FlyyDropAsk) -> Void)?

    /// A request was answered, cancelled or ran out, so its prompt can go.
    var onAskEnded: ((String) -> Void)?

    /// One of our FlyyDrops moved on, keyed by the device it goes to.
    var onSend: ((String, FlyyDropSend) -> Void)?

    /// Shows what was sent or received in Recent.
    var onActivity: ((Activity) -> Void)?

    private struct Pending {
        let urls: [URL]
        let text: String?
    }

    private let share: SharePlugin
    private let devices: DeviceRegistry
    private let lock = NSLock()
    private var pending: [String: Pending] = [:]
    private var asking: [String: String] = [:]

    init(share: SharePlugin, devices: DeviceRegistry) {
        self.share = share
        self.devices = devices
    }

    // MARK: Sending

    /// Asks `device` to take `urls` and/or `text`. Call off the main thread.
    func send(_ urls: [URL], text: String? = nil, to device: Device) {
        let items = urls.compactMap { url -> FlyyDrop.Item? in
            guard let size = (try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.int64Value else { return nil }

            return FlyyDrop.Item(name: url.lastPathComponent, size: size)
        }
        guard !items.isEmpty || !(text ?? "").isEmpty else { return }

        let packet = drop.request(to: device.id, items: items, text: text)
        guard let id = packet.string("id") else { return }

        lock.withLock { pending[id] = Pending(urls: urls, text: text) }
        onSend?(device.id, FlyyDropSend(id: id, stage: .waiting))
        device.send(packet)
    }

    /// Stops waiting for an answer to `id`.
    func cancel(_ id: String, to deviceId: String) {
        lock.withLock { _ = pending.removeValue(forKey: id) }

        if let packet = drop.cancel(id) { devices[deviceId]?.send(packet) }
    }

    private func answered(_ answer: FlyyDrop.Answer, by device: Device) {
        let id = answer.offer.id
        guard let sent = lock.withLock({ pending.removeValue(forKey: id) }) else { return }

        guard answer.accepted else {
            onSend?(device.id, FlyyDropSend(id: id, stage: .declined, reason: answer.reason))
            return
        }

        if let text = sent.text {
            device.send(FlyyDrop.text(id, text))
            onActivity?(Activity(.text, title: "“\(text.prefix(60))”", detail: "FlyyDropped to \(device.name)"))
        }

        guard !sent.urls.isEmpty else {
            onSend?(device.id, FlyyDropSend(id: id, stage: .sent))
            return
        }

        sendFiles(sent.urls, id: id, total: answer.offer.totalSize, to: device)
    }

    /// Offers every accepted file at once and reports progress across all of them.
    private func sendFiles(_ urls: [URL], id: String, total: Int64, to device: Device) {
        let progress = TransferProgress(total: total)
        let group = DispatchGroup()
        let failed = Locked(false)
        let deviceId = device.id

        onSend?(deviceId, FlyyDropSend(id: id, stage: .sending, progress: progress.snapshot()))

        for url in urls {
            group.enter()
            share.send(url, to: device, flyyDropId: id, onBytes: { [weak self] bytes in
                guard let reading = progress.advance(bytes) else { return }

                self?.onSend?(deviceId, FlyyDropSend(id: id, stage: .sending, progress: reading))
            }, onDone: { error in
                if error != nil { failed.set(true) }
                group.leave()
            })
        }

        group.notify(queue: .global()) { [weak self] in
            self?.onSend?(deviceId, FlyyDropSend(id: id, stage: failed.value ? .failed : .sent))
        }
    }

    // MARK: Receiving

    func accept(_ id: String) {
        answer(id, with: drop.accept(id))
    }

    func decline(_ id: String) {
        answer(id, with: drop.decline(id))
    }

    private func answer(_ id: String, with packet: NetworkPacket?) {
        guard let deviceId = lock.withLock({ asking.removeValue(forKey: id) }) else { return }

        onAskEnded?(id)
        if let packet { devices[deviceId]?.send(packet) }
    }

    private func requested(_ packet: NetworkPacket, by device: Device) {
        switch drop.onRequest(from: device.id, packet, isPaired: device.isPaired) {
        case .ask(let offer):
            lock.withLock { asking[offer.id] = device.id }
            onAsk?(FlyyDropAsk(offer: offer, deviceName: device.name, deviceType: device.link.identity.deviceType, isPaired: device.isPaired))
        case .reply(let reply):
            device.send(reply)
        case nil:
            break
        }
    }

    // MARK: Plugin

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        switch packet.type {
        case PacketType.flyyDropRequest:
            requested(packet, by: device)
        case PacketType.flyyDropResponse:
            if let answer = drop.onResponse(from: device.id, packet) { answered(answer, by: device) }
        case PacketType.flyyDropCancel:
            if let offer = drop.onCancel(from: device.id, packet) { ended(offer) }
        default:
            break
        }
    }

    /// Call every few seconds: drops requests nobody answered.
    func tick() {
        drop.expire().forEach(ended)
    }

    private func ended(_ offer: FlyyDrop.Offer) {
        if lock.withLock({ asking.removeValue(forKey: offer.id) }) != nil { onAskEnded?(offer.id) }
        if lock.withLock({ pending.removeValue(forKey: offer.id) }) != nil {
            onSend?(offer.deviceId, FlyyDropSend(id: offer.id, stage: .noAnswer))
        }
    }
}

/// A value shared between threads.
private final class Locked<Value>: @unchecked Sendable {
    private var stored: Value
    private let lock = NSLock()

    init(_ value: Value) {
        stored = value
    }

    var value: Value { lock.withLock { stored } }

    func set(_ value: Value) {
        lock.withLock { stored = value }
    }
}
