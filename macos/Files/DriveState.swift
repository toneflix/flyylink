import FileProvider
import Foundation

/// What Finder has been shown of one phone: a lasting identifier for each path, so renames and moves keep an item's
/// identity, and the last listing of each folder opened, so changes made on the phone can be found by listing again.
/// Saved in the extension's container between launches. Safe to use from any thread.
final class DriveState {
    struct Change {
        var updated: [PhoneDriveMessage.Item] = []
        var deleted: [NSFileProviderItemIdentifier] = []

        var isEmpty: Bool { updated.isEmpty && deleted.isEmpty }
    }

    private struct Folder: Codable {
        var items: [PhoneDriveMessage.Item]
        var seen: Date
    }

    private struct Saved: Codable {
        var ids: [String: String] = [:]
        var folders: [String: Folder] = [:]
        var anchor: Int = 0
    }

    /// How many opened folders are checked again for changes.
    private let watchedLimit = 40

    private var saved: Saved
    private var paths: [String: String]
    private let file: URL
    private let lock = NSLock()

    init(domain: String) {
        let folder = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)

        file = folder.appendingPathComponent("drive-\(domain.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "phone").json")
        saved = (try? Data(contentsOf: file)).flatMap { try? JSONDecoder().decode(Saved.self, from: $0) } ?? Saved()
        paths = Dictionary(saved.ids.map { ($0.value, $0.key) }, uniquingKeysWith: { first, _ in first })
    }

    var anchor: NSFileProviderSyncAnchor {
        lock.withLock { NSFileProviderSyncAnchor(Data(String(saved.anchor).utf8)) }
    }

    /// The folders opened most recently, which Finder is most likely still showing.
    var watchedFolders: [String] {
        lock.withLock {
            saved.folders.sorted { $0.value.seen > $1.value.seen }.prefix(watchedLimit).map(\.key)
        }
    }

    func path(for identifier: NSFileProviderItemIdentifier) -> String? {
        if identifier == .rootContainer { return "/" }

        return lock.withLock { saved.ids[identifier.rawValue] }
    }

    func identifier(for path: String) -> NSFileProviderItemIdentifier {
        if path == "/" { return .rootContainer }

        return lock.withLock { identifierLocked(for: path) }
    }

    /// The item at `path` from the last listing of its folder.
    func cached(_ path: String) -> PhoneDriveMessage.Item? {
        let parent = (path as NSString).deletingLastPathComponent

        return lock.withLock { saved.folders[parent]?.items.first { $0.path == path } }
    }

    func item(_ item: PhoneDriveMessage.Item) -> DriveItem {
        DriveItem(item, identifier: identifier(for: item.path), parent: identifier(for: item.parent))
    }

    /// Takes a fresh listing of `folder` and returns what changed since the last one.
    @discardableResult
    func listed(_ folder: String, items: [PhoneDriveMessage.Item]) -> Change {
        lock.withLock {
            let old = saved.folders[folder]?.items ?? []
            let fresh = Set(items.map(\.path))
            var change = Change()

            change.updated = items.filter { !old.contains($0) }
            for gone in old where !fresh.contains(gone.path) {
                if let id = paths[gone.path] { change.deleted.append(NSFileProviderItemIdentifier(id)) }
                forgetLocked(gone.path)
            }
            items.forEach { _ = identifierLocked(for: $0.path) }

            saved.folders[folder] = Folder(items: items, seen: Date())
            trimLocked()
            if !change.isEmpty { saved.anchor += 1 }
            saveLocked()

            return change
        }
    }

    /// The folder is gone from the phone.
    func vanished(_ folder: String) -> NSFileProviderItemIdentifier? {
        lock.withLock {
            let id = paths[folder].map { NSFileProviderItemIdentifier($0) }
            forgetLocked(folder)
            saved.folders[(folder as NSString).deletingLastPathComponent]?.items.removeAll { $0.path == folder }
            saved.anchor += 1
            saveLocked()

            return id
        }
    }

    /// Records an item made or changed from Finder, so the next listing doesn't report it again.
    func stored(_ item: PhoneDriveMessage.Item) {
        lock.withLock {
            _ = identifierLocked(for: item.path)
            if var folder = saved.folders[item.parent] {
                folder.items.removeAll { $0.path == item.path }
                folder.items.append(item)
                saved.folders[item.parent] = folder
            }
            saveLocked()
        }
    }

    /// Keeps the identifiers of the item and everything in it after a rename or move from Finder.
    func moved(from old: String, to new: String) {
        lock.withLock {
            let prefix = old + "/"
            for (id, path) in saved.ids where path == old || path.hasPrefix(prefix) {
                let moved = new + path.dropFirst(old.count)
                saved.ids[id] = moved
                paths.removeValue(forKey: path)
                paths[moved] = id
            }

            saved.folders[(old as NSString).deletingLastPathComponent]?.items.removeAll { $0.path == old }
            for (path, folder) in saved.folders where path == old || path.hasPrefix(prefix) {
                saved.folders.removeValue(forKey: path)
                saved.folders[new + path.dropFirst(old.count)] = folder
            }
            saveLocked()
        }
    }

    /// Forgets an item deleted from Finder, and everything in it.
    func deleted(_ path: String) {
        lock.withLock {
            forgetLocked(path)
            saved.folders[(path as NSString).deletingLastPathComponent]?.items.removeAll { $0.path == path }
            saveLocked()
        }
    }

    func save() {
        lock.withLock { saveLocked() }
    }

    private func identifierLocked(for path: String) -> NSFileProviderItemIdentifier {
        if let id = paths[path] { return NSFileProviderItemIdentifier(id) }

        let id = UUID().uuidString
        saved.ids[id] = path
        paths[path] = id

        return NSFileProviderItemIdentifier(id)
    }

    private func forgetLocked(_ path: String) {
        let prefix = path + "/"

        for (known, id) in paths where known == path || known.hasPrefix(prefix) {
            paths.removeValue(forKey: known)
            saved.ids.removeValue(forKey: id)
        }
        for folder in saved.folders.keys where folder == path || folder.hasPrefix(prefix) {
            saved.folders.removeValue(forKey: folder)
        }
    }

    /// Keeps the saved listings to the folders opened most recently.
    private func trimLocked() {
        let limit = watchedLimit * 4
        guard saved.folders.count > limit else { return }

        let old = saved.folders.sorted { $0.value.seen > $1.value.seen }.dropFirst(limit).map(\.key)
        old.forEach { saved.folders.removeValue(forKey: $0) }
    }

    private func saveLocked() {
        try? JSONEncoder().encode(saved).write(to: file, options: .atomic)
    }
}
