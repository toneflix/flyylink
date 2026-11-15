import FileProvider
import UniformTypeIdentifiers

/// A file or folder on the phone, as Finder sees it.
final class DriveItem: NSObject, NSFileProviderItem {
    let itemIdentifier: NSFileProviderItemIdentifier
    let parentItemIdentifier: NSFileProviderItemIdentifier
    let filename: String
    let contentType: UTType
    let documentSize: NSNumber?
    let contentModificationDate: Date?
    let capabilities: NSFileProviderItemCapabilities
    let itemVersion: NSFileProviderItemVersion

    init(_ item: PhoneDriveMessage.Item, identifier: NSFileProviderItemIdentifier, parent: NSFileProviderItemIdentifier) {
        itemIdentifier = identifier
        parentItemIdentifier = parent
        filename = identifier == .rootContainer ? "Phone" : item.name
        contentType = item.isFolder ? .folder : Self.type(of: item.name)
        documentSize = item.isFolder ? nil : NSNumber(value: item.size)
        contentModificationDate = item.modified > 0 ? Date(timeIntervalSince1970: TimeInterval(item.modified) / 1000) : nil

        var capabilities: NSFileProviderItemCapabilities = [.allowsReading, .allowsWriting, .allowsRenaming, .allowsReparenting, .allowsDeleting]
        if item.isFolder { capabilities.formUnion([.allowsAddingSubItems, .allowsContentEnumerating]) }
        self.capabilities = capabilities

        let version = Data("\(item.size):\(item.modified)".utf8)
        itemVersion = NSFileProviderItemVersion(contentVersion: version, metadataVersion: version)
    }

    static let root = DriveItem(
        PhoneDriveMessage.Item(path: "/", isFolder: true, size: 0, modified: 0),
        identifier: .rootContainer,
        parent: .rootContainer
    )

    /// Bundles such as .app are folders on a Mac, but arrive from the phone as single files.
    private static func type(of name: String) -> UTType {
        guard let type = UTType(filenameExtension: (name as NSString).pathExtension), !type.conforms(to: .package) else { return .data }

        return type
    }
}
