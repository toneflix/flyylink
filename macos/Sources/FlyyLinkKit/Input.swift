import Foundation

/// A side of a computer's screen, where the pointer crosses to another computer.
public enum ScreenEdge: String, Sendable, CaseIterable {
    case left, right, top, bottom

    public var opposite: ScreenEdge {
        switch self {
        case .left: return .right
        case .right: return .left
        case .top: return .bottom
        case .bottom: return .top
        }
    }

    /// Where along this edge a point is, from 0 at the top or left to 1 at the bottom or right.
    public func position(x: Double, y: Double, inMinX minX: Double, minY: Double, maxX: Double, maxY: Double) -> Double {
        let fraction: Double
        switch self {
        case .left, .right: fraction = maxY > minY ? (y - minY) / (maxY - minY) : 0.5
        case .top, .bottom: fraction = maxX > minX ? (x - minX) / (maxX - minX) : 0.5
        }
        return min(max(fraction, 0), 1)
    }

    /// The point just inside this edge at `position`, `inset` away from it.
    public func point(at position: Double, minX: Double, minY: Double, maxX: Double, maxY: Double, inset: Double = 2) -> (x: Double, y: Double) {
        let along = min(max(position, 0), 1)
        switch self {
        case .left: return (minX + inset, minY + along * (maxY - minY))
        case .right: return (maxX - inset, minY + along * (maxY - minY))
        case .top: return (minX + along * (maxX - minX), minY + inset)
        case .bottom: return (minX + along * (maxX - minX), maxY - inset)
        }
    }

    /// True when the point is at or past this edge.
    public func reached(x: Double, y: Double, minX: Double, minY: Double, maxX: Double, maxY: Double) -> Bool {
        switch self {
        case .left: return x <= minX
        case .right: return x >= maxX
        case .top: return y <= minY
        case .bottom: return y >= maxY
        }
    }
}

/// Keyboard and mouse shared from one computer to another ("flyylink.input"). The controlling computer sends `enter`
/// when its pointer leaves through an edge, then moves, buttons, scrolls and keys. The controlled computer sends
/// `leave` when its pointer goes back out through the edge it came in by. A phone sends `begin` instead and works the
/// computer with its touchpad and keyboard. Keys use the W3C `code` names, such as
/// "KeyA" or "ShiftLeft", so each platform maps them to its own key codes. Mirrors Input.kt and Input.cs.
public enum RemoteInput: Equatable, Sendable {
    /// The pointer arrives at the receiver's `edge`, `position` along it. `platform` is the sender's ("macos" or "windows").
    case enter(edge: ScreenEdge, position: Double, platform: String)
    /// The sender works the receiver without an edge, as a phone does. The receiver answers with its own `begin`, so the
    /// sender learns its `platform`, and `ready` is false while it can't play input.
    case begin(platform: String, ready: Bool)
    /// The pointer goes back to the sender, leaving the receiver through `edge` at `position`.
    case leave(edge: ScreenEdge, position: Double)
    /// The sender stopped controlling; let go of anything still held.
    case release
    /// Moves the pointer by this many points.
    case move(dx: Double, dy: Double)
    /// Moves the pointer to a fraction of the receiver's main screen, 0 to 1 from the top left.
    case moveTo(x: Double, y: Double)
    case button(MouseButton, down: Bool)
    /// Scrolls by this many points; positive `dy` scrolls up and positive `dx` right.
    case scroll(dx: Double, dy: Double)
    case key(code: String, down: Bool)
    /// Types `text` as it is, whatever the receiver's keyboard layout.
    case text(String)

    public enum MouseButton: String, Sendable {
        case left, right, middle
    }

    public static let macOS = "macos"
    public static let windows = "windows"
    public static let android = "android"

    /// Larger moves or scrolls than this in one packet are treated as nonsense.
    private static let maxStep = 10_000.0

    /// Longer text than this in one packet is treated as nonsense.
    public static let maxText = 1000

    public func toPacket() -> NetworkPacket {
        let body: [String: JSONValue]
        switch self {
        case .enter(let edge, let position, let platform):
            body = ["action": "enter", "edge": .string(edge.rawValue), "position": .double(position), "platform": .string(platform)]
        case .begin(let platform, let ready):
            body = ["action": "begin", "platform": .string(platform), "ready": .bool(ready)]
        case .leave(let edge, let position):
            body = ["action": "leave", "edge": .string(edge.rawValue), "position": .double(position)]
        case .release:
            body = ["action": "release"]
        case .move(let dx, let dy):
            body = ["action": "move", "dx": .double(dx), "dy": .double(dy)]
        case .moveTo(let x, let y):
            body = ["action": "moveTo", "x": .double(x), "y": .double(y)]
        case .button(let button, let down):
            body = ["action": "button", "button": .string(button.rawValue), "down": .bool(down)]
        case .scroll(let dx, let dy):
            body = ["action": "scroll", "dx": .double(dx), "dy": .double(dy)]
        case .key(let code, let down):
            body = ["action": "key", "code": .string(code), "down": .bool(down)]
        case .text(let text):
            body = ["action": "text", "text": .string(text)]
        }
        return NetworkPacket(type: PacketType.input, body: body)
    }

    public init?(packet: NetworkPacket) {
        guard packet.type == PacketType.input else { return nil }

        func fraction(_ key: String) -> Double? {
            packet.double(key).flatMap { (0...1).contains($0) ? $0 : nil }
        }
        func step(_ key: String) -> Double? {
            packet.double(key).flatMap { $0.isFinite && abs($0) <= Self.maxStep ? $0 : nil }
        }

        switch packet.string("action") {
        case "enter":
            guard let edge = packet.string("edge").flatMap(ScreenEdge.init(rawValue:)), let position = fraction("position") else { return nil }
            self = .enter(edge: edge, position: position, platform: packet.string("platform") ?? "")
        case "begin":
            self = .begin(platform: packet.string("platform") ?? "", ready: packet.bool("ready") ?? true)
        case "leave":
            guard let edge = packet.string("edge").flatMap(ScreenEdge.init(rawValue:)), let position = fraction("position") else { return nil }
            self = .leave(edge: edge, position: position)
        case "release":
            self = .release
        case "move":
            guard let dx = step("dx"), let dy = step("dy") else { return nil }
            self = .move(dx: dx, dy: dy)
        case "moveTo":
            guard let x = fraction("x"), let y = fraction("y") else { return nil }
            self = .moveTo(x: x, y: y)
        case "button":
            guard let button = packet.string("button").flatMap(MouseButton.init(rawValue:)), let down = packet.bool("down") else { return nil }
            self = .button(button, down: down)
        case "scroll":
            guard let dx = step("dx"), let dy = step("dy") else { return nil }
            self = .scroll(dx: dx, dy: dy)
        case "key":
            guard let code = packet.string("code"), KeyCodes.all.contains(code), let down = packet.bool("down") else { return nil }
            self = .key(code: code, down: down)
        case "text":
            // Counted like Kotlin and C# strings, in UTF-16 units.
            guard let text = packet.string("text"), !text.isEmpty, text.utf16.count <= Self.maxText else { return nil }
            self = .text(text)
        default:
            return nil
        }
    }
}

/// The W3C `code` names keys travel under, and the ones that act as modifiers.
public enum KeyCodes {
    public static let letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".map { "Key\($0)" }
    public static let digits = (0...9).map { "Digit\($0)" }
    public static let functionKeys = (1...12).map { "F\($0)" }
    public static let numpad = (0...9).map { "Numpad\($0)" } + ["NumpadDecimal", "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadEnter"]
    public static let modifiers: Set<String> = ["ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight"]
    public static let others = [
        "Enter", "Escape", "Backspace", "Tab", "Space", "Minus", "Equal", "BracketLeft", "BracketRight", "Backslash",
        "Semicolon", "Quote", "Backquote", "Comma", "Period", "Slash", "IntlBackslash", "CapsLock",
        "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown", "Delete", "Insert", "ContextMenu",
    ]
    public static let media = ["AudioVolumeUp", "AudioVolumeDown", "AudioVolumeMute", "MediaPlayPause", "MediaTrackNext", "MediaTrackPrevious"]

    public static let all: Set<String> = Set(letters + digits + functionKeys + numpad + others + media).union(modifiers)

    /// Swaps Control and Meta, so Ctrl+C on a PC is Command-C on a Mac and the other way round.
    public static func swappingControlAndMeta(_ code: String) -> String {
        switch code {
        case "ControlLeft": return "MetaLeft"
        case "ControlRight": return "MetaRight"
        case "MetaLeft": return "ControlLeft"
        case "MetaRight": return "ControlRight"
        default: return code
        }
    }
}
