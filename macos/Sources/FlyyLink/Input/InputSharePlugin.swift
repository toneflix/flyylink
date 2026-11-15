import Foundation
import FlyyLinkKit

/// Shares this Mac's keyboard and mouse with paired computers, and lets them and paired phones work it ("flyylink.input").
final class InputSharePlugin: Plugin {
    let incoming: Set<String> = [PacketType.input]

    let capture = InputCapture()
    let injector = InputInjector()

    /// Called on the main thread when the edges, or who is working which computer, change.
    var onChange: (() -> Void)?

    private let devices: DeviceRegistry
    private let defaults = UserDefaults.standard
    private static let edgesKey = "inputEdges"
    private static let allowsKey = "allowsRemoteInput"

    init(devices: DeviceRegistry) {
        self.devices = devices

        let send: (String, RemoteInput) -> Bool = { [weak devices] id, input in
            guard let device = devices?[id], device.isPaired else { return false }
            return device.send(input.toPacket())
        }
        capture.send = send
        injector.send = send
        capture.edges = { [weak self] in self?.connectedEdges ?? [:] }
        let injector = injector
        capture.isControlled = { [weak injector] in injector?.controllerId != nil }
        capture.onChange = { [weak self] _ in self?.onChange?() }
        injector.onChange = { [weak self] _ in self?.onChange?() }

        if !edges.isEmpty { capture.start() }
    }

    /// Whether paired computers and phones may work this Mac.
    var allowsControl: Bool {
        get { defaults.object(forKey: Self.allowsKey) as? Bool ?? true }
        set {
            defaults.set(newValue, forKey: Self.allowsKey)
            if !newValue, let id = injector.controllerId { injector.lost(id) }
            onChange?()
        }
    }

    /// macOS has to allow FlyyLink to watch and post keyboard and mouse events.
    var hasAccess: Bool { InputCapture.hasAccess(prompting: false) }

    func edge(for deviceId: String) -> ScreenEdge? {
        edges[deviceId]
    }

    /// Puts a computer beyond one edge of this Mac, or nil to stop sharing with it. Call on the main thread.
    func setEdge(_ edge: ScreenEdge?, for deviceId: String) {
        var all = edges
        all[deviceId] = edge
        defaults.set(all.mapValues(\.rawValue), forKey: Self.edgesKey)

        if all.isEmpty {
            capture.stop()
        } else if !capture.start() {
            _ = InputCapture.hasAccess(prompting: true)
        }
        onChange?()
    }

    /// Tries again after Accessibility access was given. Call on the main thread.
    func retryAccess() {
        if !edges.isEmpty, !capture.start() { _ = InputCapture.hasAccess(prompting: true) }
        onChange?()
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        guard let input = RemoteInput(packet: packet) else { return }

        let deviceId = device.id
        // The main queue keeps the moves in order, which a Task would not.
        DispatchQueue.main.async { [self] in
            if case .leave(let edge, let position) = input {
                capture.resume(from: deviceId, edge: edge, position: position)
                return
            }
            if case .begin = input {
                _ = device.send(RemoteInput.begin(platform: RemoteInput.macOS, ready: allowsControl && hasAccess).toPacket())
            }
            guard allowsControl else { return }

            switch input {
            case .enter(let edge, _, _):
                learn(edge, for: deviceId)
                if !hasAccess { _ = InputCapture.hasAccess(prompting: true) }
            case .begin:
                if !hasAccess { _ = InputCapture.hasAccess(prompting: true) }
            default:
                break
            }
            injector.handle(input, from: deviceId)
        }
    }

    /// The device disconnected. Call on the main thread.
    func forget(_ deviceId: String) {
        capture.lost(deviceId)
        injector.lost(deviceId)
    }

    private var edges: [String: ScreenEdge] {
        let stored = defaults.dictionary(forKey: Self.edgesKey) as? [String: String] ?? [:]
        return stored.compactMapValues(ScreenEdge.init(rawValue:))
    }

    private var connectedEdges: [String: ScreenEdge] {
        edges.filter { devices[$0.key]?.isPaired == true }
    }

    /// A computer that came in through an edge sits beyond it, so the pointer can go back the same way.
    private func learn(_ edge: ScreenEdge, for deviceId: String) {
        guard edges[deviceId] == nil, !edges.values.contains(edge) else { return }

        setEdge(edge, for: deviceId)
    }
}
