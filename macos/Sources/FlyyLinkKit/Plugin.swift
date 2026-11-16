import Foundation

/// A feature that handles some packet types for paired devices. Mirrors Plugin.kt.
/// Methods may be called from a networking thread.
public protocol Plugin: AnyObject {
    /// Packet types this plugin accepts.
    var incoming: Set<String> { get }

    /// The device just became paired, or connected while already paired.
    func onPaired(_ device: Device)

    /// The device turned a feature on or off; see `PhoneFeatures`.
    func onFeaturesChanged(_ device: Device)

    func onPacket(_ packet: NetworkPacket, from device: Device)
}

public extension Plugin {
    func onPaired(_ device: Device) {}

    func onFeaturesChanged(_ device: Device) {}
}

/// Hands each packet to the plugins that accept its type.
public final class PluginRouter {
    private let plugins: [Plugin]

    public init(_ plugins: [Plugin]) {
        self.plugins = plugins
    }

    public var incomingCapabilities: Set<String> {
        plugins.reduce(into: Set<String>()) { $0.formUnion($1.incoming) }
    }

    public func onPaired(_ device: Device) {
        plugins.forEach { $0.onPaired(device) }
    }

    public func onFeaturesChanged(_ device: Device) {
        plugins.forEach { $0.onFeaturesChanged(device) }
    }

    /// Returns false when no plugin accepts the packet.
    @discardableResult
    public func dispatch(_ packet: NetworkPacket, from device: Device) -> Bool {
        let targets = plugins.filter { $0.incoming.contains(packet.type) }
        targets.forEach { $0.onPacket(packet, from: device) }

        return !targets.isEmpty
    }
}
