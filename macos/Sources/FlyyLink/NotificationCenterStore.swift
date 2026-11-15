import Foundation
import SQLite3

/// Reads what Notification Center is showing from its own database. macOS has no API for other apps'
/// notifications, and the database can only be read with Full Disk Access.
final class NotificationCenterStore {
    struct Record: Equatable {
        let id: Int64
        let bundleId: String
        let title: String
        let subtitle: String
        let body: String
        let delivered: Date
    }

    private static let query = """
        SELECT record.rec_id, app.identifier, record.data, record.delivered_date
        FROM record JOIN app ON app.app_id = record.app_id
        ORDER BY record.delivered_date DESC LIMIT ?
        """

    /// macOS 15 and later keep the database in a group container; earlier versions in the user's temporary folder.
    private static var locations: [URL] {
        let home = FileManager.default.homeDirectoryForCurrentUser
        var urls = [home.appendingPathComponent("Library/Group Containers/group.com.apple.usernoted/db2/db")]

        var buffer = [CChar](repeating: 0, count: Int(PATH_MAX))
        if confstr(_CS_DARWIN_USER_DIR, &buffer, buffer.count) > 0 {
            let userDir = URL(fileURLWithPath: String(cString: buffer), isDirectory: true)
            urls.append(userDir.appendingPathComponent("com.apple.notificationcenter/db2/db"))
        }

        return urls
    }

    /// The newest notifications, newest first, or nil when the database can't be read.
    func read(limit: Int = 100) -> [Record]? {
        for url in Self.locations where FileManager.default.fileExists(atPath: url.path) {
            if let records = read(url, limit: limit) { return records }
        }

        return nil
    }

    private func read(_ url: URL, limit: Int) -> [Record]? {
        var db: OpaquePointer?
        defer { sqlite3_close(db) }
        guard sqlite3_open_v2(url.path, &db, SQLITE_OPEN_READONLY, nil) == SQLITE_OK else { return nil }

        var statement: OpaquePointer?
        defer { sqlite3_finalize(statement) }
        guard sqlite3_prepare_v2(db, Self.query, -1, &statement, nil) == SQLITE_OK else { return nil }

        sqlite3_bind_int(statement, 1, Int32(limit))

        var records: [Record] = []
        while true {
            let step = sqlite3_step(statement)
            if step == SQLITE_DONE { break }
            guard step == SQLITE_ROW else { return nil }

            if let record = Self.record(statement) { records.append(record) }
        }

        return records
    }

    private static func record(_ statement: OpaquePointer?) -> Record? {
        guard let identifier = sqlite3_column_text(statement, 1),
              let bytes = sqlite3_column_blob(statement, 2)
        else { return nil }

        let data = Data(bytes: bytes, count: Int(sqlite3_column_bytes(statement, 2)))
        guard let plist = try? PropertyListSerialization.propertyList(from: data, format: nil) as? [String: Any],
              let request = plist["req"] as? [String: Any]
        else { return nil }

        return Record(
            id: sqlite3_column_int64(statement, 0),
            bundleId: String(cString: identifier),
            title: request["titl"] as? String ?? "",
            subtitle: request["subt"] as? String ?? "",
            body: request["body"] as? String ?? "",
            delivered: Date(timeIntervalSinceReferenceDate: sqlite3_column_double(statement, 3))
        )
    }
}
