import FileProvider

/// Lists a folder on the phone for Finder, and finds what changed on the phone since Finder last looked.
/// The working set covers every folder opened recently.
final class DriveEnumerator: NSObject, NSFileProviderEnumerator {
    private let container: NSFileProviderItemIdentifier
    private let state: DriveState
    private let client: DriveClient

    init(container: NSFileProviderItemIdentifier, state: DriveState, client: DriveClient) {
        self.container = container
        self.state = state
        self.client = client
    }

    func invalidate() {}

    func enumerateItems(for observer: NSFileProviderEnumerationObserver, startingAt page: NSFileProviderPage) {
        guard container != .workingSet else {
            observer.finishEnumerating(upTo: nil)
            return
        }
        guard let path = state.path(for: container) else {
            observer.finishEnumeratingWithError(NSFileProviderError(.noSuchItem))
            return
        }

        Task {
            do {
                let items = try await client.send(.list, path: path).files ?? []
                state.listed(path, items: items)

                observer.didEnumerate(items.map(state.item))
                observer.finishEnumerating(upTo: nil)
            } catch {
                observer.finishEnumeratingWithError(error)
            }
        }
    }

    func enumerateChanges(for observer: NSFileProviderChangeObserver, from anchor: NSFileProviderSyncAnchor) {
        let folders: [String]
        if container == .workingSet {
            folders = state.watchedFolders
        } else {
            folders = state.path(for: container).map { [$0] } ?? []
        }

        Task {
            var change = DriveState.Change()

            for folder in folders {
                do {
                    let items = try await client.send(.list, path: folder).files ?? []
                    let found = state.listed(folder, items: items)
                    change.updated += found.updated
                    change.deleted += found.deleted
                } catch let error as NSFileProviderError where error.code == .noSuchItem {
                    if let gone = state.vanished(folder) { change.deleted.append(gone) }
                } catch {
                    // The phone is away or said no; Finder keeps what it has until the next check.
                    break
                }
            }

            if !change.updated.isEmpty { observer.didUpdate(change.updated.map(state.item)) }
            if !change.deleted.isEmpty { observer.didDeleteItems(withIdentifiers: change.deleted) }
            observer.finishEnumeratingChanges(upTo: state.anchor, moreComing: false)
        }
    }

    func currentSyncAnchor(completionHandler: @escaping (NSFileProviderSyncAnchor?) -> Void) {
        completionHandler(state.anchor)
    }
}
