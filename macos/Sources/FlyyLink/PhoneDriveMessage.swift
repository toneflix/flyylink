import Foundation

/// What FlyyLink and its Finder extension say to each other about a phone's storage, one line of JSON each way over
/// a local socket. Built into both the app and the extension.
enum PhoneDriveMessage {
    enum Action: String, Codable {
        case list, download, upload, folder, move, delete
    }

    /// A file or folder in the phone's storage, as `PhoneFile` describes it.
    struct Item: Codable, Hashable {
        var path: String
        var isFolder: Bool
        var size: Int64
        /// Milliseconds since 1970.
        var modified: Int64

        var name: String { (path as NSString).lastPathComponent }

        var parent: String { (path as NSString).deletingLastPathComponent }
    }

    struct Request: Codable {
        var token: String
        /// The phone's device id, which is also its File Provider domain.
        var phone: String
        var action: Action
        var path: String
        var to: String?
        /// For uploads, the file to send, in `uploads`.
        var file: String?
    }

    struct Reply: Codable {
        /// `offline` when the phone isn't connected, else a `PhoneFiles.Failure`.
        var error: String?
        var files: [Item]?
        var file: Item?
        /// For downloads, where the file was saved, in `downloads`.
        var saved: String?

        static let offline = "offline"
    }

    /// Where the app listens, and the secret every request carries.
    struct Endpoint: Codable {
        var port: UInt16
        var token: String

        static func load() -> Endpoint? {
            guard let data = try? Data(contentsOf: PhoneDriveMessage.endpointFile) else { return nil }

            return try? JSONDecoder().decode(Endpoint.self, from: data)
        }

        func save() throws {
            try FileManager.default.createDirectory(at: PhoneDriveMessage.folder, withIntermediateDirectories: true)
            try JSONEncoder().encode(self).write(to: PhoneDriveMessage.endpointFile, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: PhoneDriveMessage.endpointFile.path)
        }
    }

    static func join(_ folder: String, _ name: String) -> String {
        folder == "/" ? "/" + name : folder + "/" + name
    }

    /// The extension's sandbox can reach this one folder; its own home is a container the app can't see.
    static var folder: URL {
        let home = getpwuid(getuid()).flatMap { String(validatingUTF8: $0.pointee.pw_dir) } ?? NSHomeDirectory()

        return URL(fileURLWithPath: home).appendingPathComponent("Library/Application Support/FlyyLink/Files", isDirectory: true)
    }

    static var endpointFile: URL { folder.appendingPathComponent("endpoint.json") }

    static var uploads: URL { folder.appendingPathComponent("Uploads", isDirectory: true) }

    static var downloads: URL { folder.appendingPathComponent("Downloads", isDirectory: true) }
}
