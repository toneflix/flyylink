import FileProvider
import Foundation
import UniformTypeIdentifiers

/// One phone's storage as a location in Finder. macOS starts one per phone, named by its domain (the phone's device id),
/// and every request goes to the phone through FlyyLink.
final class FileProviderExtension: NSObject, NSFileProviderReplicatedExtension {
    private let domain: NSFileProviderDomain
    private let state: DriveState
    private let client: DriveClient

    required init(domain: NSFileProviderDomain) {
        self.domain = domain
        state = DriveState(domain: domain.identifier.rawValue)
        client = DriveClient(phone: domain.identifier.rawValue)
        super.init()
    }

    func invalidate() {
        state.save()
    }

    func item(
        for identifier: NSFileProviderItemIdentifier,
        request: NSFileProviderRequest,
        completionHandler: @escaping (NSFileProviderItem?, Error?) -> Void
    ) -> Progress {
        if identifier == .rootContainer {
            completionHandler(DriveItem.root, nil)
            return Progress()
        }
        guard let path = state.path(for: identifier) else {
            completionHandler(nil, NSFileProviderError(.noSuchItem))
            return Progress()
        }
        if let cached = state.cached(path) {
            completionHandler(state.item(cached), nil)
            return Progress()
        }

        return run(completionHandler) { [self] in
            let parent = (path as NSString).deletingLastPathComponent
            let items = try await client.send(.list, path: parent).files ?? []
            state.listed(parent, items: items)

            guard let item = items.first(where: { $0.path == path }) else { throw NSFileProviderError(.noSuchItem) }
            return state.item(item)
        }
    }

    func fetchContents(
        for itemIdentifier: NSFileProviderItemIdentifier,
        version requestedVersion: NSFileProviderItemVersion?,
        request: NSFileProviderRequest,
        completionHandler: @escaping (URL?, NSFileProviderItem?, Error?) -> Void
    ) -> Progress {
        let progress = Progress(totalUnitCount: 1)

        guard let path = state.path(for: itemIdentifier) else {
            completionHandler(nil, nil, NSFileProviderError(.noSuchItem))
            return progress
        }

        Task {
            do {
                let reply = try await client.send(.download, path: path)
                guard let saved = reply.saved, let file = reply.file else { throw NSFileProviderError(.noSuchItem) }

                let url = try handOver(URL(fileURLWithPath: saved))
                state.stored(file)
                progress.completedUnitCount = 1
                completionHandler(url, state.item(file), nil)
            } catch {
                completionHandler(nil, nil, error)
            }
        }

        return progress
    }

    func createItem(
        basedOn itemTemplate: NSFileProviderItem,
        fields: NSFileProviderItemFields,
        contents url: URL?,
        options: NSFileProviderCreateItemOptions = [],
        request: NSFileProviderRequest,
        completionHandler: @escaping (NSFileProviderItem?, NSFileProviderItemFields, Bool, Error?) -> Void
    ) -> Progress {
        let isFolder = itemTemplate.contentType == .folder

        guard let parent = state.path(for: itemTemplate.parentItemIdentifier) else {
            completionHandler(nil, [], false, NSFileProviderError(.noSuchItem))
            return Progress()
        }
        if itemTemplate.contentType == .symbolicLink || itemTemplate.contentType?.conforms(to: .package) == true {
            completionHandler(nil, [], false, CocoaError(.featureUnsupported))
            return Progress()
        }

        let path = PhoneDriveMessage.join(parent, itemTemplate.filename)

        return change(completionHandler) { [self] in
            if isFolder { return try await makeFolder(path) }

            return try await upload(url, to: path)
        }
    }

    func modifyItem(
        _ item: NSFileProviderItem,
        baseVersion version: NSFileProviderItemVersion,
        changedFields: NSFileProviderItemFields,
        contents newContents: URL?,
        options: NSFileProviderModifyItemOptions = [],
        request: NSFileProviderRequest,
        completionHandler: @escaping (NSFileProviderItem?, NSFileProviderItemFields, Bool, Error?) -> Void
    ) -> Progress {
        guard let path = state.path(for: item.itemIdentifier), let parent = state.path(for: item.parentItemIdentifier) else {
            completionHandler(nil, [], false, NSFileProviderError(.noSuchItem))
            return Progress()
        }

        let destination = PhoneDriveMessage.join(parent, item.filename)
        let moves = destination != path && (changedFields.contains(.filename) || changedFields.contains(.parentItemIdentifier))
        let replaces = changedFields.contains(.contents) && newContents != nil

        return change(completionHandler) { [self] in
            var current = state.cached(path)

            if moves {
                current = try await client.send(.move, path: path, to: destination).file
                state.moved(from: path, to: destination)
            }
            if replaces {
                return try await upload(newContents, to: destination)
            }

            return current ?? PhoneDriveMessage.Item(
                path: destination,
                isFolder: item.contentType == .folder,
                size: item.documentSize??.int64Value ?? 0,
                modified: Int64((item.contentModificationDate??.timeIntervalSince1970 ?? 0) * 1000)
            )
        }
    }

    func deleteItem(
        identifier: NSFileProviderItemIdentifier,
        baseVersion version: NSFileProviderItemVersion,
        options: NSFileProviderDeleteItemOptions = [],
        request: NSFileProviderRequest,
        completionHandler: @escaping (Error?) -> Void
    ) -> Progress {
        guard let path = state.path(for: identifier), path != "/" else {
            completionHandler(NSFileProviderError(.noSuchItem))
            return Progress()
        }

        Task {
            do {
                _ = try await client.send(.delete, path: path)
                state.deleted(path)
                completionHandler(nil)
            } catch {
                completionHandler(error)
            }
        }

        return Progress()
    }

    func enumerator(for containerItemIdentifier: NSFileProviderItemIdentifier, request: NSFileProviderRequest) throws -> NSFileProviderEnumerator {
        guard containerItemIdentifier == .workingSet || state.path(for: containerItemIdentifier) != nil else {
            throw NSFileProviderError(.noSuchItem)
        }

        return DriveEnumerator(container: containerItemIdentifier, state: state, client: client)
    }

    // MARK: Steps

    /// A folder that already exists counts as made, so copying a folder onto one merges them.
    private func makeFolder(_ path: String) async throws -> PhoneDriveMessage.Item {
        do {
            guard let made = try await client.send(.folder, path: path).file else { throw CocoaError(.fileWriteUnknown) }
            return made
        } catch let error as NSFileProviderError where error.code == .filenameCollision {
            return PhoneDriveMessage.Item(path: path, isFolder: true, size: 0, modified: Int64(Date().timeIntervalSince1970 * 1000))
        }
    }

    /// Copies the file where FlyyLink can read it and has it sent to `path`. An item with no contents arrives empty.
    private func upload(_ contents: URL?, to path: String) async throws -> PhoneDriveMessage.Item {
        let files = FileManager.default
        let copy = PhoneDriveMessage.uploads.appendingPathComponent(UUID().uuidString)

        try files.createDirectory(at: PhoneDriveMessage.uploads, withIntermediateDirectories: true)
        if let contents {
            try files.copyItem(at: contents, to: copy)
        } else {
            try Data().write(to: copy)
        }
        defer { try? files.removeItem(at: copy) }

        guard let sent = try await client.send(.upload, path: path, file: copy).file else { throw CocoaError(.fileWriteUnknown) }
        return sent
    }

    /// Moves a download out of the shared folder into one only this extension uses, which macOS then takes over.
    private func handOver(_ saved: URL) throws -> URL {
        let folder = try NSFileProviderManager(for: domain)?.temporaryDirectoryURL() ?? FileManager.default.temporaryDirectory
        let target = folder.appendingPathComponent(UUID().uuidString)

        do {
            try FileManager.default.moveItem(at: saved, to: target)
        } catch {
            try? FileManager.default.removeItem(at: saved)
            throw error
        }

        return target
    }

    private func run(
        _ completionHandler: @escaping (NSFileProviderItem?, Error?) -> Void,
        _ work: @escaping () async throws -> NSFileProviderItem
    ) -> Progress {
        Task {
            do {
                completionHandler(try await work(), nil)
            } catch {
                completionHandler(nil, error)
            }
        }

        return Progress()
    }

    /// Runs a change made in Finder and reports the item as the phone now has it.
    private func change(
        _ completionHandler: @escaping (NSFileProviderItem?, NSFileProviderItemFields, Bool, Error?) -> Void,
        _ work: @escaping () async throws -> PhoneDriveMessage.Item
    ) -> Progress {
        Task { [self] in
            do {
                let item = try await work()
                state.stored(item)
                completionHandler(state.item(item), [], false, nil)
            } catch {
                completionHandler(nil, [], false, error)
            }
        }

        return Progress()
    }
}
