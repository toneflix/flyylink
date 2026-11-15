import Foundation

/// Mirroring the phone's screen on the Mac. The Mac asks with `start`; the phone answers with an
/// "flyylink.screen" packet whose payload is a live H.264 stream of `ScreenFrame`s. When the Mac asks for sound and
/// the phone can record it, the stream also carries `ScreenFrame.audio` frames of 16-bit little-endian PCM at
/// `audioRate` Hz with `audioChannels` channels, interleaved. Mirrors Screen.kt.
public enum Screen {
    /// The payload size of a live stream: read until the sender closes it.
    public static let stream: Int64 = -1

    /// Windows' H.264 decoder takes at most 2304 pixels on a side.
    public static let maxSize: Int64 = 2304
    public static let bitrate: Int64 = 20_000_000
    public static let audioRate: Int64 = 48_000
    public static let audioChannels: Int64 = 2

    /// How the pictures are encoded. Phones send H.264; computers send each picture as a JPEG.
    public static let h264 = "h264"
    public static let jpeg = "jpeg"

    public enum Action: String, Sendable {
        case start
        case stop
    }

    public struct Request: Equatable, Sendable {
        public var action: Action
        public var maxSize: Int64
        public var bitrate: Int64
        public var audio: Bool

        public init(action: Action, maxSize: Int64 = Screen.maxSize, bitrate: Int64 = Screen.bitrate, audio: Bool = false) {
            self.action = action
            self.maxSize = maxSize
            self.bitrate = bitrate
            self.audio = audio
        }
    }

    /// What the device is sending. Sizes are in pixels; `running` is false once mirroring stops.
    /// `audioRate` and `audioChannels` are 0 when the stream has no sound. A computer also says which
    /// `platform` it is (`RemoteInput.macOS` or `RemoteInput.windows`), so the viewer can swap Control and Command.
    public struct Info: Equatable, Sendable {
        public var width: Int64
        public var height: Int64
        public var running: Bool
        public var audioRate: Int64
        public var audioChannels: Int64
        public var codec: String
        public var platform: String?

        public init(
            width: Int64,
            height: Int64,
            running: Bool,
            audioRate: Int64 = 0,
            audioChannels: Int64 = 0,
            codec: String = Screen.h264,
            platform: String? = nil
        ) {
            self.width = width
            self.height = height
            self.running = running
            self.audioRate = audioRate
            self.audioChannels = audioChannels
            self.codec = codec
            self.platform = platform
        }

        public var hasAudio: Bool { audioRate > 0 && audioChannels > 0 }
    }

    public static func start(maxSize: Int64 = Screen.maxSize, bitrate: Int64 = Screen.bitrate, audio: Bool = false) -> NetworkPacket {
        var body: [String: JSONValue] = [
            "action": .string(Action.start.rawValue),
            "maxSize": .int(maxSize),
            "bitrate": .int(bitrate),
        ]
        if audio { body["audio"] = .bool(true) }
        return NetworkPacket(type: PacketType.screenRequest, body: body)
    }

    public static func stop() -> NetworkPacket {
        NetworkPacket(type: PacketType.screenRequest, body: ["action": .string(Action.stop.rawValue)])
    }

    public static func request(_ packet: NetworkPacket) -> Request? {
        guard packet.type == PacketType.screenRequest, let action = packet.string("action").flatMap(Action.init(rawValue:)) else {
            return nil
        }

        return Request(
            action: action,
            maxSize: min(max(packet.int("maxSize") ?? maxSize, 320), 4096),
            bitrate: min(max(packet.int("bitrate") ?? bitrate, 500_000), 50_000_000),
            audio: packet.bool("audio") ?? false
        )
    }

    /// Announces a stream; send it with `stream` as the payload size.
    public static func started(width: Int64, height: Int64, audio: Bool = false, codec: String = Screen.h264, platform: String? = nil) -> NetworkPacket {
        var body: [String: JSONValue] = [
            "width": .int(width),
            "height": .int(height),
            "codec": .string(codec),
            "running": .bool(true),
        ]
        if let platform { body["platform"] = .string(platform) }
        if audio {
            body["audioCodec"] = .string("pcm16le")
            body["audioRate"] = .int(audioRate)
            body["audioChannels"] = .int(audioChannels)
        }
        return NetworkPacket(type: PacketType.screen, body: body)
    }

    public static func stopped() -> NetworkPacket {
        NetworkPacket(type: PacketType.screen, body: ["running": .bool(false)])
    }

    public static func info(_ packet: NetworkPacket) -> Info? {
        guard packet.type == PacketType.screen else { return nil }

        let pcm = packet.string("audioCodec") == "pcm16le"
        return Info(
            width: packet.int("width") ?? 0,
            height: packet.int("height") ?? 0,
            running: packet.bool("running") ?? false,
            audioRate: pcm ? min(max(packet.int("audioRate") ?? 0, 0), 192_000) : 0,
            audioChannels: pcm ? min(max(packet.int("audioChannels") ?? 0, 0), 8) : 0,
            codec: packet.string("codec") ?? h264,
            platform: packet.string("platform")
        )
    }
}

/// One encoded piece of the screen stream. On the wire each is a 13 byte header (size, then the
/// presentation time in microseconds, then `flags`, all big-endian) followed by H.264 in Annex B form (or one JPEG
/// picture, flagged `key`, from a computer), or by PCM sound when `isAudio`.
public struct ScreenFrame: Equatable, Sendable {
    public static let headerSize = 13
    public static let config: UInt8 = 1
    public static let key: UInt8 = 2
    public static let audio: UInt8 = 4

    /// Larger than any real frame; anything bigger means the stream is corrupt.
    public static let maxSize = 16 * 1024 * 1024

    public var data: Data
    public var timeUs: Int64
    public var flags: UInt8

    public init(data: Data, timeUs: Int64, flags: UInt8) {
        self.data = data
        self.timeUs = timeUs
        self.flags = flags
    }

    /// The SPS and PPS the decoder needs before any picture.
    public var isConfig: Bool { flags & Self.config != 0 }
    public var isKey: Bool { flags & Self.key != 0 }
    public var isAudio: Bool { flags & Self.audio != 0 }

    public func header() -> Data {
        var header = Data()
        withUnsafeBytes(of: UInt32(data.count).bigEndian) { header.append(contentsOf: $0) }
        withUnsafeBytes(of: timeUs.bigEndian) { header.append(contentsOf: $0) }
        header.append(flags)
        return header
    }
}

/// Collects stream bytes, however they are split, back into whole frames. Mirrors ScreenFrame.Reader.
public final class ScreenFrameReader {
    private var pending = Data()

    public init() {}

    public func feed(_ bytes: Data) throws -> [ScreenFrame] {
        pending.append(bytes)

        var frames: [ScreenFrame] = []
        var offset = pending.startIndex

        while pending.endIndex - offset >= ScreenFrame.headerSize {
            let size = Int(pending.bigEndian(UInt32.self, at: offset))
            guard size <= ScreenFrame.maxSize else { throw ProtocolError("screen frame of \(size) bytes") }

            let time = Int64(bitPattern: pending.bigEndian(UInt64.self, at: offset + 4))
            let flags = pending[offset + 12]
            let start = offset + ScreenFrame.headerSize
            guard pending.endIndex - start >= size else { break }

            frames.append(ScreenFrame(data: pending.subdata(in: start..<start + size), timeUs: time, flags: flags))
            offset = start + size
        }

        pending = pending.subdata(in: offset..<pending.endIndex)
        return frames
    }
}

private extension Data {
    func bigEndian<T: FixedWidthInteger>(_ type: T.Type, at offset: Index) -> T {
        var value: T = 0
        for index in offset..<offset + MemoryLayout<T>.size {
            value = value << 8 | T(self[index])
        }
        return value
    }
}

/// A touch, key or typed text from the Mac. Positions are fractions of the screen, 0 to 1 from the top left.
/// Mirrors ScreenInput in Screen.kt.
public enum ScreenInput: Equatable, Sendable {
    case tap(x: Double, y: Double)
    case longPress(x: Double, y: Double)
    case swipe(x: Double, y: Double, toX: Double, toY: Double, durationMs: Int64)
    case text(String)
    /// A key that is not text. With `select`, a movement key extends the selection like Shift does.
    case key(String, select: Bool = false)

    public static let back = "back"
    public static let home = "home"
    public static let recents = "recents"
    public static let notifications = "notifications"
    public static let enter = "enter"
    public static let delete = "delete"
    public static let deleteForward = "deleteForward"
    public static let deleteWord = "deleteWord"
    public static let tab = "tab"
    public static let left = "left"
    public static let right = "right"
    public static let up = "up"
    public static let down = "down"
    public static let wordLeft = "wordLeft"
    public static let wordRight = "wordRight"
    public static let lineStart = "lineStart"
    public static let lineEnd = "lineEnd"
    public static let selectAll = "selectAll"
    public static let copy = "copy"
    public static let cut = "cut"

    public static let keys: Set<String> = [
        back, home, recents, notifications, enter, delete, deleteForward, deleteWord, tab,
        left, right, up, down, wordLeft, wordRight, lineStart, lineEnd, selectAll, copy, cut,
    ]

    public func toPacket() -> NetworkPacket {
        let body: [String: JSONValue]
        switch self {
        case .tap(let x, let y):
            body = ["action": "tap", "x": .double(x), "y": .double(y)]
        case .longPress(let x, let y):
            body = ["action": "longPress", "x": .double(x), "y": .double(y)]
        case .swipe(let x, let y, let toX, let toY, let duration):
            body = ["action": "swipe", "x": .double(x), "y": .double(y), "toX": .double(toX), "toY": .double(toY), "duration": .int(duration)]
        case .text(let text):
            body = ["action": "text", "text": .string(text)]
        case .key(let key, let select):
            body = select ? ["action": "key", "key": .string(key), "select": .bool(true)] : ["action": "key", "key": .string(key)]
        }
        return NetworkPacket(type: PacketType.screenInput, body: body)
    }

    public init?(packet: NetworkPacket) {
        guard packet.type == PacketType.screenInput else { return nil }

        func fraction(_ key: String) -> Double? {
            packet.double(key).flatMap { (0...1).contains($0) ? $0 : nil }
        }

        switch packet.string("action") {
        case "tap":
            guard let x = fraction("x"), let y = fraction("y") else { return nil }
            self = .tap(x: x, y: y)
        case "longPress":
            guard let x = fraction("x"), let y = fraction("y") else { return nil }
            self = .longPress(x: x, y: y)
        case "swipe":
            guard let x = fraction("x"), let y = fraction("y"), let toX = fraction("toX"), let toY = fraction("toY") else { return nil }
            self = .swipe(x: x, y: y, toX: toX, toY: toY, durationMs: min(max(packet.int("duration") ?? 200, 10), 5000))
        case "text":
            guard let text = packet.string("text"), !text.isEmpty else { return nil }
            self = .text(text)
        case "key":
            guard let key = packet.string("key"), Self.keys.contains(key) else { return nil }
            self = .key(key, select: packet.bool("select") ?? false)
        default:
            return nil
        }
    }
}
