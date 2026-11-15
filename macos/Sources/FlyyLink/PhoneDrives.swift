import AppKit
import FileProvider
import FlyyLinkKit
import OSLog

/// Each paired phone's storage as a location in Finder's sidebar. The Finder extension does the browsing and asks
/// through `PhoneDriveBridge`; this side passes its requests to the phone and keeps one File Provider domain per phone.
final class PhoneDrives {
    private let files: FilesPlugin
    private let devices: DeviceRegistry
    private let bridge = PhoneDriveBridge()
    private let log = Logger(subsystem: "com.toneflix.flyylink", category: "drive")
    private var shown: Set<String> = []
    private var timer: Timer?

    /// How often Finder checks the phone for changes made on it.
    private let refresh: TimeInterval = 60

    init(files: FilesPlugin, devices: DeviceRegistry) {
        self.files = files
        self.devices = devices
        bridge.handle = { [weak self] request, reply in self?.handle(request, reply: reply) }
    }

    func start() {
        bridge.start()
        timer = Timer.scheduledTimer(withTimeInterval: refresh, repeats: true) { [weak self] _ in self?.signalConnected() }
    }

    func stop() {
        bridge.stop()
        timer?.invalidate()
        timer = nil
    }

    /// Shows a location for each phone that can share its files.
    func show(_ phones: [(id: String, name: String)]) {
        for phone in phones where !shown.contains(phone.id) {
            shown.insert(phone.id)
            NSFileProviderManager.add(Self.domain(phone.id, name: phone.name)) { [weak self] error in
                if let error {
                    self?.log.error("Could not add \(phone.name, privacy: .public) to Finder: \(error.localizedDescription, privacy: .public)")
                } else {
                    self?.signal(phone.id)
                }
            }
        }
    }

    /// Removes the locations of phones no longer paired.
    func prune(keeping paired: Set<String>) {
        NSFileProviderManager.getDomainsWithCompletionHandler { [weak self] domains, _ in
            for domain in domains where !paired.contains(domain.identifier.rawValue) {
                self?.remove(domain)
            }
        }
    }

    func forget(_ phoneId: String) {
        shown.remove(phoneId)
        NSFileProviderManager.getDomainsWithCompletionHandler { [weak self] domains, _ in
            domains.filter { $0.identifier.rawValue == phoneId }.forEach { self?.remove($0) }
        }
    }

    /// Opens the phone's storage in Finder.
    func open(_ phoneId: String) {
        let domain = NSFileProviderDomain(identifier: NSFileProviderDomainIdentifier(rawValue: phoneId), displayName: "")

        NSFileProviderManager(for: domain)?.getUserVisibleURL(for: .rootContainer) { url, _ in
            guard let url else { return }

            DispatchQueue.main.async { NSWorkspace.shared.open(url) }
        }
    }

    /// Asks Finder to look again at what it shows of this phone.
    func signal(_ phoneId: String) {
        let domain = NSFileProviderDomain(identifier: NSFileProviderDomainIdentifier(rawValue: phoneId), displayName: "")

        NSFileProviderManager(for: domain)?.signalEnumerator(for: .workingSet) { _ in }
    }

    private func signalConnected() {
        devices.all.filter { $0.isPaired && shown.contains($0.id) }.forEach { signal($0.id) }
    }

    private func remove(_ domain: NSFileProviderDomain) {
        NSFileProviderManager.remove(domain) { [weak self] error in
            if let error { self?.log.error("Could not remove a phone from Finder: \(error.localizedDescription, privacy: .public)") }
        }
    }

    private static func domain(_ id: String, name: String) -> NSFileProviderDomain {
        let domain = NSFileProviderDomain(identifier: NSFileProviderDomainIdentifier(rawValue: id), displayName: name)
        domain.supportsSyncingTrash = false

        return domain
    }

    // MARK: Requests from Finder

    private func handle(_ request: PhoneDriveMessage.Request, reply: @escaping (PhoneDriveMessage.Reply) -> Void) {
        guard let device = devices[request.phone], device.isPaired, let action = PhoneFiles.Action(rawValue: request.action.rawValue) else {
            reply(PhoneDriveMessage.Reply(error: PhoneDriveMessage.Reply.offline))
            return
        }

        let upload = request.file.flatMap(Self.inside(PhoneDriveMessage.uploads))
        if action == .upload, upload == nil {
            reply(PhoneDriveMessage.Reply(error: PhoneFiles.Failure.invalid.rawValue))
            return
        }

        let saveTo = action == .download ? PhoneDriveMessage.downloads.appendingPathComponent(UUID().uuidString) : nil

        files.send(action, path: request.path, to: request.to, upload: upload, saveTo: saveTo, device: device) { result in
            switch result {
            case .success(let answer):
                reply(PhoneDriveMessage.Reply(
                    files: answer.files.map(Self.item),
                    file: answer.file.map(Self.item),
                    saved: saveTo?.path
                ))
            case .failure(.offline):
                reply(PhoneDriveMessage.Reply(error: PhoneDriveMessage.Reply.offline))
            case .failure(.phone(let failure)):
                reply(PhoneDriveMessage.Reply(error: failure.rawValue))
            }
        }
    }

    /// `path` when it is a file directly in `folder`, so the extension can only have files sent from there.
    private static func inside(_ folder: URL) -> (String) -> URL? {
        { path in
            let url = URL(fileURLWithPath: path).standardizedFileURL
            return url.deletingLastPathComponent().path == folder.standardizedFileURL.path ? url : nil
        }
    }

    private static func item(_ file: PhoneFile) -> PhoneDriveMessage.Item {
        PhoneDriveMessage.Item(path: file.path, isFolder: file.isFolder, size: file.size, modified: file.modified)
    }
}
