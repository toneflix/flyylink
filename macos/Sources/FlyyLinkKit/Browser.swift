import Foundation

/// How the browser add-on reaches FlyyLink. The browser starts FlyyLink's host, a small helper inside the app, and
/// talks to it over native messaging. The host passes every message on to the app over a local port, as one line of
/// JSON each, after a first line carrying the endpoint's token. Mirrors Browser.cs.
public enum Browser {
    /// The native messaging host's name, which the add-on connects to.
    public static let hostName = "com.toneflix.flyylink"

    public static let firefoxId = "browser@flyylink.toneflix.net"

    /// Chromium add-on ids allowed to start the host: the one the unpacked add-on gets from its key, then each store's.
    public static let chromiumIds = ["dnlkpekmfbjelcenommpiibkcdbooacj"]

    /// Chrome drops the host when it sends a bigger message than this.
    public static let maxMessage = 1024 * 1024

    /// The host manifest that tells a browser where the host is and which add-ons may start it.
    public static func hostManifest(hostPath: String, firefox: Bool) -> Data {
        var manifest: [String: Any] = [
            "name": hostName,
            "description": "FlyyLink",
            "path": hostPath,
            "type": "stdio",
        ]
        if firefox {
            manifest["allowed_extensions"] = [firefoxId]
        } else {
            manifest["allowed_origins"] = chromiumIds.map { "chrome-extension://\($0)/" }
        }

        return (try? JSONSerialization.data(withJSONObject: manifest, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])) ?? Data()
    }

    /// A number from a tel: or sms: link or a page, with only what a phone dials. Nil when it can't be a number.
    public static func cleanNumber(_ text: String) -> String? {
        var trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        for scheme in ["tel:", "sms:", "callto:"] where trimmed.lowercased().hasPrefix(scheme) {
            trimmed = String(trimmed.dropFirst(scheme.count))
            trimmed = trimmed.removingPercentEncoding ?? trimmed
        }
        trimmed = String(trimmed.split(whereSeparator: { "?;,".contains($0) }, omittingEmptySubsequences: false).first ?? "")

        var number = ""
        for character in trimmed {
            if character.isASCII && character.isNumber || character == "*" || character == "#" || (character == "+" && number.isEmpty) {
                number.append(character)
            }
        }
        let digits = number.filter { $0.isASCII && $0.isNumber }.count

        return (3...20).contains(digits) ? number : nil
    }
}

/// Native messaging's framing: each message is UTF-8 JSON after its length, 4 bytes in the machine's byte order,
/// which is little-endian on every computer FlyyLink runs on.
public enum NativeMessaging {
    public static func frame(_ json: String) -> Data {
        let body = Data(json.utf8)
        var length = UInt32(body.count).littleEndian
        var frame = Data(bytes: &length, count: 4)
        frame.append(body)

        return frame
    }
}

/// Collects bytes from the browser and hands back whole messages.
public struct NativeMessageReader {
    /// A browser never sends more than this in one message, so a bigger length means the stream is broken.
    public static let maxLength = 64 * 1024 * 1024

    private var buffer = Data()

    /// Set once a length made no sense; nothing more is read.
    public private(set) var isBroken = false

    public init() {}

    public mutating func append(_ bytes: Data) {
        buffer.append(bytes)
    }

    /// The next whole message, or nil until more bytes arrive.
    public mutating func next() -> String? {
        guard !isBroken, buffer.count >= 4 else { return nil }

        let start = buffer.startIndex
        let length = buffer[start..<start + 4].enumerated().reduce(UInt32(0)) { $0 | UInt32($1.element) << (8 * UInt32($1.offset)) }
        guard length <= Self.maxLength else {
            isBroken = true
            return nil
        }
        guard buffer.count >= 4 + Int(length) else { return nil }

        let body = buffer[start + 4..<start + 4 + Int(length)]
        buffer = Data(buffer[(start + 4 + Int(length))...])

        return String(decoding: body, as: UTF8.self)
    }
}

/// Where the app listens for the host, and the secret the host's first line must carry. Saved where only the
/// user can read it.
public struct BrowserEndpoint: Equatable, Sendable {
    public var port: UInt16
    public var token: String

    public init(port: UInt16, token: String) {
        self.port = port
        self.token = token
    }

    public init?(json: String) {
        guard let root = Self.object(json),
              let port = root["port"]?.intValue.flatMap({ UInt16(exactly: $0) }), port > 0,
              let token = root["token"]?.stringValue, !token.isEmpty
        else { return nil }

        self.init(port: port, token: token)
    }

    public var json: String { BrowserEvents.encode(["port": .int(Int64(port)), "token": .string(token)]) }

    /// The host's first line.
    public var helloLine: String { BrowserEvents.encode(["token": .string(token)]) }

    /// Whether a first line carries this endpoint's token.
    public func accepts(_ line: String) -> Bool {
        Self.object(line)?["token"]?.stringValue == token
    }

    /// ~/Library/Application Support/FlyyLink/Browser/endpoint.json, written by the app and read by the host.
    public static var file: URL {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support")

        return support.appendingPathComponent("FlyyLink/Browser/endpoint.json")
    }

    public static func load() -> BrowserEndpoint? {
        (try? String(contentsOf: file, encoding: .utf8)).flatMap(BrowserEndpoint.init(json:))
    }

    public func save() throws {
        let folder = Self.file.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try Data(json.utf8).write(to: Self.file, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: Self.file.path)
    }

    public static func newToken() -> String {
        (0..<16).map { _ in String(format: "%02x", UInt8.random(in: .min ... .max)) }.joined()
    }

    static func object(_ json: String) -> [String: JSONValue]? {
        try? JSONDecoder().decode([String: JSONValue].self, from: Data(json.utf8))
    }
}
