import AppKit
import Foundation
import FlyyLinkKit

/// Sends this Mac's notifications to paired phones and computers ("kdeconnect.notification"), with the app's icon,
/// and tells them when one goes away. Mirrors NotificationSharePlugin.cs.
final class NotificationSharePlugin: Plugin {
    let incoming: Set<String> = [PacketType.notificationRequest]

    private static let idPrefix = "mac."
    private static let pollInterval: TimeInterval = 3
    private static let enabledKey = "sharesNotifications"

    private let devices: DeviceRegistry
    private let store = NotificationCenterStore()
    private let icons = AppIcons()
    private let queue = DispatchQueue(label: "com.toneflix.flyylink.notification-share")
    private let lock = NSLock()
    private var timer: DispatchSourceTimer?
    /// What Notification Center showed at the last look. Nil until the first look, which sends nothing.
    private var current: [String: PhoneNotification]?
    /// "deviceId/hash" for each icon a device already has.
    private var sentIcons: Set<String> = []
    private var readable = false

    /// Called on any thread when Full Disk Access is granted or taken away.
    var onAccessChange: ((Bool) -> Void)?

    init(devices: DeviceRegistry) {
        self.devices = devices
    }

    /// Whether Notification Center could be read at the last look.
    var hasAccess: Bool { lock.withLock { readable } }

    /// Off until turned on from the settings menu.
    var isEnabled: Bool {
        get { UserDefaults.standard.bool(forKey: Self.enabledKey) }
        set { UserDefaults.standard.set(newValue, forKey: Self.enabledKey) }
    }

    func start() {
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now(), repeating: Self.pollInterval)
        timer.setEventHandler { [weak self] in self?.poll() }
        timer.resume()
        self.timer = timer
    }

    func onPaired(_ device: Device) {
        lock.withLock { sentIcons = sentIcons.filter { !$0.hasPrefix("\(device.id)/") } }
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        guard packet.bool("request") == true, isEnabled, device.link.identity.deviceType.isComputer else { return }

        queue.async { [weak self] in
            guard let self else { return }

            let all = self.lock.withLock { Array((self.current ?? [:]).values) }
            all.forEach { self.send($0, to: device) }
        }
    }

    /// Opens the Full Disk Access list in System Settings, where FlyyLink can be turned on.
    static func openAccessSettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles") else { return }

        NSWorkspace.shared.open(url)
    }

    private func poll() {
        let records = store.read()
        let wasReadable = lock.withLock { () -> Bool in
            let was = readable
            readable = records != nil
            return was
        }
        if wasReadable != (records != nil) { onAccessChange?(records != nil) }

        guard let records else { return }

        let showing = records.compactMap(notification)
        let (added, removed) = lock.withLock { () -> ([PhoneNotification], [String]) in
            let previous = current
            current = Dictionary(showing.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
            guard let previous else { return ([], []) }

            return (showing.filter { previous[$0.id] == nil }, previous.keys.filter { id in !showing.contains { $0.id == id } })
        }
        guard isEnabled, !added.isEmpty || !removed.isEmpty else { return }

        for device in devices.all where device.isPaired && Self.receives(device) {
            added.reversed().forEach { send($0, to: device) }
            removed.forEach { device.send(PhoneNotification.cancelPacket(id: $0)) }
        }
    }

    private static func receives(_ device: Device) -> Bool {
        let type = device.link.identity.deviceType
        return type.isPhone || type.isComputer
    }

    /// Our own notifications came from another device, so they stay here.
    private func notification(_ record: NotificationCenterStore.Record) -> PhoneNotification? {
        if let own = Bundle.main.bundleIdentifier, record.bundleId == own { return nil }
        if record.bundleId.hasPrefix("com.toneflix.flyylink") { return nil }

        let text = [record.subtitle, record.body].filter { !$0.isEmpty }.joined(separator: "\n")
        guard !record.title.isEmpty || !text.isEmpty else { return nil }

        let app = icons.app(record.bundleId)

        return PhoneNotification(
            id: "\(Self.idPrefix)\(record.id)",
            appName: app.name,
            title: record.title.isEmpty ? app.name : record.title,
            text: text,
            time: Int64(record.delivered.timeIntervalSince1970 * 1000),
            iconHash: app.iconHash
        )
    }

    /// Sends the notification with its app's icon, unless the device already has that icon.
    private func send(_ note: PhoneNotification, to device: Device) {
        let packet = note.toPacket()
        guard let hash = note.iconHash, let icon = icons.iconFile(hash: hash) else {
            device.send(packet)
            return
        }

        let key = "\(device.id)/\(hash)"
        let isNew = lock.withLock { sentIcons.insert(key).inserted }
        guard isNew, let handle = try? FileHandle(forReadingFrom: icon), let size = try? icon.resourceValues(forKeys: [.fileSizeKey]).fileSize else {
            device.send(packet)
            return
        }

        let offered = device.sendWithPayload(packet, size: Int64(size), source: handle) { [weak self] error in
            try? handle.close()
            guard let self, error != nil else { return }

            self.lock.withLock { _ = self.sentIcons.remove(key) }
        }
        if !offered { device.send(packet) }
    }
}
