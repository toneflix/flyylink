import Foundation

/// A file or folder in the phone's storage. `path` starts at the storage root, like "/DCIM/Camera/IMG_1.jpg"; `modified` is in milliseconds.
/// Mirrors PhoneFile in PhoneFiles.kt.
public struct PhoneFile: Equatable, Hashable, Sendable {
    public var path: String
    public var isFolder: Bool
    public var size: Int64
    public var modified: Int64

    public init(path: String, isFolder: Bool, size: Int64 = 0, modified: Int64 = 0) {
        self.path = path
        self.isFolder = isFolder
        self.size = size
        self.modified = modified
    }

    public var name: String { PhoneFiles.name(of: path) }

    public init?(json: [String: JSONValue]) {
        guard let raw = json["path"]?.stringValue, let path = PhoneFiles.clean(raw) else { return nil }

        self.init(
            path: path,
            isFolder: json["folder"]?.boolValue ?? false,
            size: json["size"]?.intValue ?? 0,
            modified: json["modified"]?.intValue ?? 0
        )
    }

    public func toJSON() -> JSONValue {
        .object([
            "path": .string(path),
            "folder": .bool(isFolder),
            "size": .int(size),
            "modified": .int(modified),
        ])
    }
}

/// Browsing and changing the phone's storage from a computer. The computer sends `filesRequest` with an action, and
/// the phone answers each with `files` carrying the same request id. Uploads carry the file as the request's payload;
/// downloads come back as the answer's payload. Mirrors PhoneFiles.kt.
public enum PhoneFiles {
    public enum Action: String, Sendable, Codable {
        case list, download, upload, folder, move, delete
    }

    /// Why a request failed. `denied` means the phone hasn't allowed computers to browse its files.
    public enum Failure: String, Error, Sendable, Codable {
        case denied, missing, exists, invalid, failed
    }

    /// `to` is where `.move` puts the item.
    public struct Request: Equatable, Sendable {
        public var id: Int64
        public var action: Action
        public var path: String
        public var to: String?

        public init(id: Int64, action: Action, path: String, to: String? = nil) {
            self.id = id
            self.action = action
            self.path = path
            self.to = to
        }
    }

    /// `files` is a folder's contents for `.list`; `file` is the item a request made, moved or sent.
    public struct Answer: Equatable, Sendable {
        public var id: Int64
        public var failure: Failure?
        public var files: [PhoneFile]
        public var file: PhoneFile?

        public init(id: Int64, failure: Failure? = nil, files: [PhoneFile] = [], file: PhoneFile? = nil) {
            self.id = id
            self.failure = failure
            self.files = files
            self.file = file
        }

        public var ok: Bool { failure == nil }
    }

    public static let root = "/"

    /// `path` with no empty or "." parts, starting with "/", or nil when it would climb out of the root.
    public static func clean(_ path: String) -> String? {
        guard !path.contains("\0") else { return nil }

        let parts = path.split(separator: "/", omittingEmptySubsequences: true).filter { $0 != "." }
        guard !parts.contains("..") else { return nil }

        return root + parts.joined(separator: "/")
    }

    public static func isValidName(_ name: String) -> Bool {
        !name.isEmpty && name != "." && name != ".." && !name.contains("/") && !name.contains("\0")
    }

    public static func join(_ folder: String, _ name: String) -> String {
        folder == root ? root + name : "\(folder)/\(name)"
    }

    public static func name(of path: String) -> String {
        path.split(separator: "/", omittingEmptySubsequences: false).last.map(String.init) ?? ""
    }

    public static func parent(of path: String) -> String {
        guard let slash = path.lastIndex(of: "/"), slash != path.startIndex else { return root }

        return String(path[..<slash])
    }

    public static func request(_ request: Request) -> NetworkPacket {
        var body: [String: JSONValue] = [
            "id": .int(request.id),
            "action": .string(request.action.rawValue),
            "path": .string(request.path),
        ]
        if let to = request.to { body["to"] = .string(to) }

        return NetworkPacket(type: PacketType.filesRequest, body: body)
    }

    public static func request(_ packet: NetworkPacket) -> Request? {
        guard packet.type == PacketType.filesRequest,
              let id = packet.int("id"),
              let action = packet.string("action").flatMap(Action.init(rawValue:)),
              let path = packet.string("path").flatMap(clean)
        else { return nil }

        var to: String?
        if let raw = packet.string("to") {
            guard let cleaned = clean(raw) else { return nil }
            to = cleaned
        }
        if action == .move, to == nil || to == root { return nil }

        return Request(id: id, action: action, path: path, to: to)
    }

    public static func answer(_ answer: Answer) -> NetworkPacket {
        var body: [String: JSONValue] = [
            "id": .int(answer.id),
            "ok": .bool(answer.ok),
        ]
        if let failure = answer.failure { body["error"] = .string(failure.rawValue) }
        if !answer.files.isEmpty { body["files"] = .array(answer.files.map { $0.toJSON() }) }
        if let file = answer.file { body["file"] = file.toJSON() }

        return NetworkPacket(type: PacketType.files, body: body)
    }

    public static func answer(_ packet: NetworkPacket) -> Answer? {
        guard packet.type == PacketType.files, let id = packet.int("id") else { return nil }

        let ok = packet.bool("ok") ?? false
        let files = (packet.body["files"]?.arrayValue ?? []).compactMap { $0.objectValue.flatMap(PhoneFile.init(json:)) }

        return Answer(
            id: id,
            failure: ok ? nil : packet.string("error").flatMap(Failure.init(rawValue:)) ?? .failed,
            files: files,
            file: packet.body["file"]?.objectValue.flatMap(PhoneFile.init(json:))
        )
    }
}
