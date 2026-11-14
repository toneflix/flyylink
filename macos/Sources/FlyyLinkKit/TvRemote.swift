import Foundation

/// A press on the remote a phone shows for a TV ("flyylink.remote"): a key, text typed into the TV's
/// focused field, or the touchpad's pointer moves, clicks and scrolls. Moves and scrolls are in the phone's dp.
/// Mirrors TvRemote.kt and TvRemote.cs.
public enum TvRemote: Equatable, Sendable {
    case key(String)
    case text(String)
    case move(dx: Double, dy: Double)
    case click
    case scroll(dx: Double, dy: Double)

    public static let up = "up"
    public static let down = "down"
    public static let left = "left"
    public static let right = "right"
    public static let select = "select"
    public static let back = "back"
    public static let home = "home"
    public static let menu = "menu"
    public static let playPause = "playPause"
    public static let next = "next"
    public static let previous = "previous"
    public static let volumeUp = "volumeUp"
    public static let volumeDown = "volumeDown"
    public static let mute = "mute"
    public static let delete = "delete"
    public static let enter = "enter"

    public static let keys: Set<String> = [
        up, down, left, right, select, back, home, menu, playPause, next, previous, volumeUp, volumeDown, mute, delete, enter,
    ]

    /// Longer text than this in one packet is treated as nonsense.
    public static let maxText = 1000

    /// A bigger move or scroll than this in one packet is treated as nonsense.
    public static let maxStep = 2000.0

    public func toPacket() -> NetworkPacket {
        switch self {
        case .key(let key):
            return NetworkPacket(type: PacketType.remote, body: ["action": "key", "key": .string(key)])
        case .text(let text):
            return NetworkPacket(type: PacketType.remote, body: ["action": "text", "text": .string(text)])
        case .move(let dx, let dy):
            return NetworkPacket(type: PacketType.remote, body: ["action": "move", "dx": .double(dx), "dy": .double(dy)])
        case .click:
            return NetworkPacket(type: PacketType.remote, body: ["action": "click"])
        case .scroll(let dx, let dy):
            return NetworkPacket(type: PacketType.remote, body: ["action": "scroll", "dx": .double(dx), "dy": .double(dy)])
        }
    }

    public init?(packet: NetworkPacket) {
        guard packet.type == PacketType.remote else { return nil }

        switch packet.string("action") {
        case "key":
            guard let key = packet.string("key"), Self.keys.contains(key) else { return nil }
            self = .key(key)
        case "text":
            guard let text = packet.string("text"), !text.isEmpty, text.count <= Self.maxText else { return nil }
            self = .text(text)
        case "move":
            guard let (dx, dy) = Self.step(packet) else { return nil }
            self = .move(dx: dx, dy: dy)
        case "click":
            self = .click
        case "scroll":
            guard let (dx, dy) = Self.step(packet) else { return nil }
            self = .scroll(dx: dx, dy: dy)
        default:
            return nil
        }
    }

    private static func step(_ packet: NetworkPacket) -> (Double, Double)? {
        guard let dx = packet.double("dx"), let dy = packet.double("dy"),
              dx.isFinite, dy.isFinite, abs(dx) <= maxStep, abs(dy) <= maxStep
        else { return nil }

        return (dx, dy)
    }
}
