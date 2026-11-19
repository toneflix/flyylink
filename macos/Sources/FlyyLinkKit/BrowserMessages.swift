import Foundation

/// Something the add-on asks of the app. `id` comes back on the answer. Mirrors BrowserMessages.cs.
public struct BrowserRequest: Equatable, Sendable {
    public static let maxText = 10_000
    public static let maxUrl = 8_192

    public enum Kind: Equatable, Sendable {
        /// The add-on says which browser it runs in.
        case hello(browser: String)
        case share(device: String, item: BrowserShare)
        /// Calls a number from the phone. Without a device, the phone the app shows.
        case call(device: String?, number: String)
        case text(device: String?, number: String, body: String)
        /// Plays a video or song at `url` on a TV that `TvFinder` found.
        case playOnTv(tv: String, url: String, title: String, mime: String?)
        /// Everything playing in the browser's tabs, which replaces what it said before.
        case media([BrowserPlayer])
    }

    public var id: String?
    public var kind: Kind

    public init(id: String? = nil, _ kind: Kind) {
        self.id = id
        self.kind = kind
    }

    public init?(line: String) {
        guard let root = BrowserEndpoint.object(line) else { return nil }

        let string = { (key: String) in root[key]?.stringValue }
        let kind: Kind?
        switch string("type") {
        case "hello":
            kind = string("browser").flatMap { (1...100).contains($0.count) ? .hello(browser: $0) : nil }
        case "share":
            if let device = string("device"), !device.isEmpty, let item = BrowserShare(root) {
                kind = .share(device: device, item: item)
            } else {
                kind = nil
            }
        case "call":
            kind = string("number").flatMap(Browser.cleanNumber).map { .call(device: string("device"), number: $0) }
        case "text":
            if let number = string("number").flatMap(Browser.cleanNumber), let body = string("body"), (1...Self.maxText).contains(body.count) {
                kind = .text(device: string("device"), number: number, body: body)
            } else {
                kind = nil
            }
        case "playOnTv":
            if let tv = string("tv"), !tv.isEmpty, let url = Self.webUrl(string("url")) {
                kind = .playOnTv(tv: tv, url: url, title: Self.limit(string("title"), 300) ?? "Video", mime: Self.limit(string("mime"), 100))
            } else {
                kind = nil
            }
        case "media":
            kind = root["players"]?.arrayValue.map { players in
                .media(Array(players.compactMap { $0.objectValue.flatMap(BrowserPlayer.init) }.prefix(20)))
            }
        default:
            kind = nil
        }

        guard let kind else { return nil }

        self.init(id: Self.limit(string("id"), 100), kind)
    }

    /// An http or https address of a sensible length, or nil.
    static func webUrl(_ text: String?) -> String? {
        guard let text, text.utf16.count <= maxUrl, let url = URL(string: text),
              let scheme = url.scheme?.lowercased(), scheme == "http" || scheme == "https", url.host != nil
        else { return nil }

        return text
    }

    static func limit(_ text: String?, _ length: Int) -> String? {
        guard let text else { return nil }

        return text.count <= length ? text : String(text.prefix(length))
    }
}

/// What the add-on sends to a device: a page or link to open, text, a file on this computer, or a file to download first.
public enum BrowserShare: Equatable, Sendable {
    case link(String)
    case text(String)
    case file(String)
    case fetch(url: String, name: String?)

    init?(_ root: [String: JSONValue]) {
        if let link = BrowserRequest.webUrl(root["link"]?.stringValue) {
            self = .link(link)
        } else if let text = root["text"]?.stringValue, (1...BrowserRequest.maxText).contains(text.count) {
            self = .text(text)
        } else if let file = root["file"]?.stringValue, file.hasPrefix("/") {
            self = .file(file)
        } else if let fetch = BrowserRequest.webUrl(root["fetch"]?.stringValue) {
            self = .fetch(url: fetch, name: Self.safeName(root["name"]?.stringValue))
        } else {
            return nil
        }
    }

    /// A file name with no folders in it.
    private static func safeName(_ name: String?) -> String? {
        guard let name else { return nil }

        let kept = name.unicodeScalars.filter { !["/", "\\", ":"].contains($0) && !CharacterSet.controlCharacters.contains($0) }
        var clean = String(kept).trimmingCharacters(in: .whitespaces)
        while clean.hasPrefix(".") { clean.removeFirst() }

        return clean.isEmpty ? nil : BrowserRequest.limit(clean, 200)
    }
}

/// A device the add-on can send to.
public struct BrowserDevice: Equatable, Sendable {
    public var id: String
    public var name: String
    public var type: String
    public var canCall: Bool
    public var canText: Bool

    public init(id: String, name: String, type: String, canCall: Bool, canText: Bool) {
        self.id = id
        self.name = name
        self.type = type
        self.canCall = canCall
        self.canText = canText
    }
}

/// A TV found on the network that plays a video from its address.
public struct BrowserTv: Equatable, Sendable {
    public var id: String
    public var name: String

    public init(id: String, name: String) {
        self.id = id
        self.name = name
    }
}

/// What the add-on shows: whether the app is ready, and where things can go.
public struct BrowserState: Equatable, Sendable {
    public static let ready = "ready"
    public static let locked = "locked"
    /// Sent by the host while the app isn't running.
    public static let closed = "closed"

    public var status: String
    public var computer: String
    public var platform: String
    public var devices: [BrowserDevice]
    public var tvs: [BrowserTv]

    public init(status: String, computer: String, platform: String, devices: [BrowserDevice], tvs: [BrowserTv]) {
        self.status = status
        self.computer = computer
        self.platform = platform
        self.devices = devices
        self.tvs = tvs
    }
}

/// What the app tells the add-on, one line of JSON each.
public enum BrowserEvents {
    public static func state(_ state: BrowserState) -> String {
        encode([
            "type": "state",
            "status": .string(state.status),
            "computer": .string(state.computer),
            "platform": .string(state.platform),
            "devices": .array(state.devices.map { device in
                .object([
                    "id": .string(device.id),
                    "name": .string(device.name),
                    "type": .string(device.type),
                    "canCall": .bool(device.canCall),
                    "canText": .bool(device.canText),
                ])
            }),
            "tvs": .array(state.tvs.map { .object(["id": .string($0.id), "name": .string($0.name)]) }),
        ])
    }

    public static func closed() -> String {
        encode(["type": "state", "status": .string(BrowserState.closed)])
    }

    /// A sign-in code that arrived on a phone.
    public static func code(_ code: String, app: String, device: String) -> String {
        encode(["type": "code", "code": .string(code), "app": .string(app), "device": .string(device)])
    }

    /// A phone call started or ended, so the add-on can pause and resume what plays.
    public static func call(active: Bool) -> String {
        encode(["type": "call", "active": .bool(active)])
    }

    /// A paired device pressed a button on one of the browser's players.
    public static func control(player: String, action: MediaAction?, seekTo: Int64?) -> String {
        var body: [String: JSONValue] = ["type": "control", "player": .string(player)]
        if let action { body["action"] = .string(action.rawValue) }
        if let seekTo { body["seekTo"] = .int(seekTo) }

        return encode(body)
    }

    public static func result(id: String, ok: Bool, error: String? = nil) -> String {
        var body: [String: JSONValue] = ["type": "result", "id": .string(id), "ok": .bool(ok)]
        if let error { body["error"] = .string(error) }

        return encode(body)
    }

    static func encode(_ body: [String: JSONValue]) -> String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        let data = (try? encoder.encode(JSONValue.object(body))) ?? Data("{}".utf8)

        return String(decoding: data, as: UTF8.self)
    }
}
