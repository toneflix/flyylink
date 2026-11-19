import AppKit
import Foundation
import OSLog
import UserNotifications
import FlyyLinkKit

/// Shows phone notifications in Notification Center. Replying, pressing a button or dismissing
/// there is sent back to the phone.
final class NotificationPlugin: NSObject, Plugin, UNUserNotificationCenterDelegate {
    let incoming: Set<String> = [PacketType.notification]

    private enum Category {
        static let plain = "phone.notification"
        static let replyable = "phone.notification.reply"
        static let missedCall = "phone.call.missed"
        static let file = "phone.file"
        static let flyyDrop = "flyydrop.ask"
    }

    private enum Key {
        static let device = "device"
        static let id = "id"
        static let replyId = "replyId"
        static let file = "file"
        static let callBack = "callBack"
        static let actions = "actions"
        static let showInFinder = "showInFinder"
        static let flyyDrop = "flyyDrop"
        static let accept = "accept"
        static let decline = "decline"
    }

    /// Notification Center shows at most this many buttons in a notification's Options menu.
    private static let maxActions = 4
    private static let actionPrefix = "action."

    private static let reply = UNTextInputNotificationAction(
        identifier: "reply",
        title: "Reply",
        options: [],
        textInputButtonTitle: "Send",
        textInputPlaceholder: "Message"
    )

    /// Every category registered so far. Each set of buttons needs its own, added the first time it is seen.
    private var categories: [String: UNNotificationCategory] = [:]

    private let log = Logger(subsystem: "com.toneflix.flyylink", category: "notifications")
    private let center: UNUserNotificationCenter?

    /// What each shown notification said, so a resend after reconnecting or an unchanged update doesn't pop up again.
    private var shown: [String: String] = [:]
    private let lock = NSLock()
    private let icons = NotificationIcons()
    private var enabled = true

    /// Paused from the menu: phone notifications are dropped instead of shown.
    var isEnabled: Bool {
        get { lock.withLock { enabled } }
        set { lock.withLock { enabled = newValue } }
    }

    /// Called after a phone notification is shown, on any thread.
    var onShown: (() -> Void)?

    /// Called on any thread with each new phone notification and the phone's name, before it shows.
    var onArrived: ((PhoneNotification, String) -> Void)?

    /// Sends replies, buttons and dismissals to a phone. Set by the app model, so they also reach phones
    /// that another computer passed on.
    var send: (String, NetworkPacket) -> Bool

    /// True when running as a bundled app. `swift run` has no bundle, and
    /// Notification Center refuses to work without one.
    static var isAvailable: Bool { Bundle.main.bundleIdentifier != nil }

    init(devices: DeviceRegistry) {
        send = { [weak devices] id, packet in devices?[id]?.send(packet) ?? false }
        center = Self.isAvailable ? UNUserNotificationCenter.current() : nil
        super.init()

        guard let center else { return }

        let base = [
            UNNotificationCategory(identifier: Category.plain, actions: [], intentIdentifiers: [], options: [.customDismissAction]),
            UNNotificationCategory(identifier: Category.replyable, actions: [Self.reply], intentIdentifiers: [], options: [.customDismissAction]),
            UNNotificationCategory(
                identifier: Category.missedCall,
                actions: [UNNotificationAction(identifier: Key.callBack, title: "Call back")],
                intentIdentifiers: [],
                options: []
            ),
            UNNotificationCategory(
                identifier: Category.flyyDrop,
                actions: [
                    UNNotificationAction(identifier: Key.accept, title: "Accept"),
                    UNNotificationAction(identifier: Key.decline, title: "Decline", options: [.destructive]),
                ],
                intentIdentifiers: [],
                options: [.customDismissAction]
            ),
            UNNotificationCategory(
                identifier: Category.file,
                actions: [UNNotificationAction(identifier: Key.showInFinder, title: "Show in Finder")],
                intentIdentifiers: [],
                options: []
            ),
        ]
        categories = Dictionary(uniqueKeysWithValues: base.map { ($0.identifier, $0) })
        center.setNotificationCategories(Set(base))
        center.delegate = self
        center.requestAuthorization(options: [.alert, .sound]) { _, _ in }
    }

    // MARK: Plugin

    func onPaired(_ device: Device) {
        device.send(NotificationRequests.requestAll())
    }

    /// Fills in what is showing on the phone once its notifications are turned on.
    func onFeaturesChanged(_ device: Device) {
        onPaired(device)
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        present(packet, deviceId: device.id, deviceName: device.name, iconsFrom: device)
    }

    /// A notification another computer passed on from its phone. Relayed notifications come without icons.
    func presentRelayed(_ packet: NetworkPacket, phoneId: String, phoneName: String) {
        present(packet, deviceId: phoneId, deviceName: phoneName, iconsFrom: nil)
    }

    private func present(_ packet: NetworkPacket, deviceId: String, deviceName: String, iconsFrom device: Device?) {
        guard let center else { return }

        if let id = PhoneNotification.cancelledId(packet) {
            let identifier = Self.identifier(device: deviceId, id: id)
            lock.withLock { _ = shown.removeValue(forKey: identifier) }
            center.removeDeliveredNotifications(withIdentifiers: [identifier])
            return
        }

        guard isEnabled, let note = PhoneNotification(packet: packet) else { return }

        let identifier = Self.identifier(device: deviceId, id: note.id)
        let summary = "\(note.title)\n\(note.text)"
        let isRepeat = lock.withLock { shown.updateValue(summary, forKey: identifier) == summary }
        guard !isRepeat else { return }

        onArrived?(note, deviceName)
        let content = UNMutableNotificationContent()
        content.title = note.title.isEmpty ? note.appName : note.title
        content.subtitle = note.title.isEmpty ? deviceName : "\(note.appName) · \(deviceName)"
        content.body = note.text
        let actions = Array(note.actions.prefix(Self.maxActions))
        content.categoryIdentifier = category(replyable: note.replyId != nil, actions: actions, in: center)
        content.userInfo = [Key.device: deviceId, Key.id: note.id, Key.replyId: note.replyId ?? "", Key.actions: actions]
        content.threadIdentifier = "\(deviceId).\(note.appName)"
        if note.silent {
            // Already on the phone before we connected: list it without a banner.
            content.interruptionLevel = .passive
        } else {
            content.sound = .default
        }

        guard let device else {
            center.add(UNNotificationRequest(identifier: identifier, content: content, trigger: nil))
            if !note.silent { onShown?() }
            return
        }

        // The phone sends the app icon, or the sender's photo, as the payload.
        icons.icon(for: packet, hash: note.iconHash, from: device) { [weak self] icon in
            if let icon, let attachment = try? UNNotificationAttachment(identifier: "icon", url: icon) {
                content.attachments = [attachment]
            }

            center.add(UNNotificationRequest(identifier: identifier, content: content, trigger: nil))
            if !note.silent { self?.onShown?() }
        }
    }

    /// The category for a notification with these buttons, registering it with Notification Center if it is new.
    private func category(replyable: Bool, actions: [String], in center: UNUserNotificationCenter) -> String {
        guard !actions.isEmpty else { return replyable ? Category.replyable : Category.plain }

        let identifier = ([replyable ? Category.replyable : Category.plain] + actions).joined(separator: "\u{1F}")
        let buttons = actions.enumerated().map { index, title in
            UNNotificationAction(identifier: Self.actionPrefix + String(index), title: title)
        }
        let category = UNNotificationCategory(
            identifier: identifier,
            actions: (replyable ? [Self.reply] : []) + buttons,
            intentIdentifiers: [],
            options: [.customDismissAction]
        )

        let all: Set<UNNotificationCategory>? = lock.withLock {
            guard categories[identifier] == nil else { return nil }

            categories[identifier] = category
            return Set(categories.values)
        }
        if let all { center.setNotificationCategories(all) }

        return identifier
    }

    /// Tells the user a file arrived. Clicking it opens the file, and its button shows it in Finder.
    func announce(file url: URL, from device: Device) {
        guard let center else { return }

        let content = UNMutableNotificationContent()
        content.title = url.lastPathComponent
        content.subtitle = "From \(device.name)"
        content.body = "Saved to Downloads"
        content.categoryIdentifier = Category.file
        content.userInfo = [Key.file: url.path]
        content.sound = .default

        center.add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }

    /// Someone nearby wants to FlyyDrop, with Accept and Decline buttons. Clicking it opens the panel to answer there.
    func announce(_ ask: FlyyDropAsk) {
        guard let center else { return }

        let content = UNMutableNotificationContent()
        content.title = "\(ask.deviceName) wants to FlyyDrop"
        content.subtitle = ask.isPaired ? "From a paired device" : "Not paired"
        content.body = ask.summary
        content.categoryIdentifier = Category.flyyDrop
        content.userInfo = [Key.flyyDrop: ask.id]
        content.sound = .default
        content.interruptionLevel = .timeSensitive

        center.add(UNNotificationRequest(identifier: Self.flyyDropIdentifier(ask.id), content: content, trigger: nil))
    }

    /// Takes the FlyyDrop notification away once it is answered or gone.
    func withdrawFlyyDrop(_ id: String) {
        let identifier = Self.flyyDropIdentifier(id)
        center?.removeDeliveredNotifications(withIdentifiers: [identifier])
        center?.removePendingNotificationRequests(withIdentifiers: [identifier])
    }

    /// A missed call, with a Call back button that dials from the phone.
    func announceMissedCall(from caller: String, number: String, deviceId: String, deviceName: String) {
        guard let center else { return }

        let content = UNMutableNotificationContent()
        content.title = "Missed call"
        content.subtitle = deviceName
        content.body = caller
        content.categoryIdentifier = Category.missedCall
        content.userInfo = [Key.device: deviceId, Key.callBack: number]
        content.sound = .default

        center.add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }

    // MARK: UNUserNotificationCenterDelegate

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        // A menu bar app is often frontmost; show banners anyway, except for quiet ones.
        let isQuiet = notification.request.content.interruptionLevel == .passive
        completionHandler(isQuiet ? [.list] : [.banner, .list, .sound])
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        defer { completionHandler() }

        let info = response.notification.request.content.userInfo
        if let link = info[ServerNotices.linkKey] as? String {
            if response.actionIdentifier != UNNotificationDismissActionIdentifier { Task { @MainActor in ServerNotices.open(link) } }
            return
        }

        if let id = info[Key.flyyDrop] as? String {
            let action = response.actionIdentifier
            Task { @MainActor in
                switch action {
                case Key.accept: AppModel.shared.acceptFlyyDrop(id)
                case Key.decline, UNNotificationDismissActionIdentifier: AppModel.shared.declineFlyyDrop(id)
                default: MenuBarPanel.toggle()
                }
            }
            return
        }

        if let path = info[Key.file] as? String {
            guard response.actionIdentifier != UNNotificationDismissActionIdentifier else { return }

            let url = URL(fileURLWithPath: path)
            let reveal = response.actionIdentifier == Key.showInFinder
            Task { @MainActor in reveal ? ReceivedFile.reveal(url) : ReceivedFile.open(url) }
            return
        }

        if let number = info[Key.callBack] as? String, response.actionIdentifier != UNNotificationDismissActionIdentifier {
            Task { @MainActor in AppModel.shared.dial(number, name: nil) }
            return
        }

        guard let deviceId = info[Key.device] as? String, let id = info[Key.id] as? String else { return }

        if let text = response as? UNTextInputNotificationResponse,
           let replyId = info[Key.replyId] as? String, !replyId.isEmpty {
            let sent = send(deviceId, NotificationRequests.reply(to: replyId, message: text.userText))
            log.info("Reply to \(id, privacy: .public) sent: \(sent)")
            return
        }

        if response.actionIdentifier.hasPrefix(Self.actionPrefix),
           let index = Int(response.actionIdentifier.dropFirst(Self.actionPrefix.count)),
           let actions = info[Key.actions] as? [String], actions.indices.contains(index) {
            let sent = send(deviceId, NotificationRequests.action(id: id, action: actions[index]))
            log.info("Pressed \(actions[index], privacy: .public) on \(id, privacy: .public): \(sent)")
            return
        }

        if response.actionIdentifier == UNNotificationDismissActionIdentifier {
            _ = send(deviceId, NotificationRequests.dismiss(id))
        }
    }

    private static func flyyDropIdentifier(_ id: String) -> String {
        "flyydrop/\(id)"
    }

    private static func identifier(device: String, id: String) -> String {
        "\(device)/\(id)"
    }
}
