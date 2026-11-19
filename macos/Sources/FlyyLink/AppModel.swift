import AppKit
import Combine
import Foundation
import OSLog
import FlyyLinkKit
import Carbon.HIToolbox
import WidgetKit

/// What the menu shows for one connected device.
struct DeviceState: Identifiable, Equatable {
    let id: String
    let name: String
    let type: DeviceType
    let host: String
    let pairState: PairState
    let code: String?
    let battery: BatteryStatus?
    /// The phone's mobile signal, when it has a SIM.
    let signal: CellSignal?
    let media: NowPlaying?
    let location: PhoneLocation?
    /// Runs FlyyLink with FlyyDrop, so files can go to it without pairing.
    var canFlyyDrop = false
    /// What the phone has turned on. Everything, for devices that don't say.
    var features = PhoneFeatures.all

    var isComputer: Bool { type.isComputer }

    var isPhone: Bool { type.isPhone }

    var isTv: Bool { type == .tv }
}

@MainActor
final class AppModel: ObservableObject {
    /// One model for the whole app, so the app delegate can reach it for tel: links.
    static let shared = AppModel()

    @Published private(set) var devices: [DeviceState] = []
    @Published private(set) var error: String?
    /// True while macOS stops FlyyLink from reaching the local network, so the phone can't be found.
    @Published private(set) var localNetworkBlocked = false
    @Published private(set) var recent: [Activity] = []
    @Published private(set) var clipboardSyncedAt: Date?
    @Published private(set) var notificationsToday = 0
    @Published private(set) var call: ActiveCall?

    /// The paired device the panel and the Share menu use: a phone or another computer. Every paired device stays connected.
    @Published private(set) var primaryId: String?

    /// The paired phone Messages, Photos and call audio use.
    @Published private(set) var phoneId: String?

    /// The device the user picked when several are paired. Kept across launches.
    var selectedPhone: String? {
        get { UserDefaults.standard.string(forKey: "selectedPhone") }
        set {
            UserDefaults.standard.set(newValue, forKey: "selectedPhone")
            publish()
        }
    }

    /// FlyyDrops from nearby devices waiting for an answer, oldest first.
    @Published private(set) var flyyDropAsks: [FlyyDropAsk] = []

    /// Our FlyyDrops, by the id of the device they go to.
    @Published private(set) var flyyDropSends: [String: FlyyDropSend] = [:]

    /// Who may FlyyDrop to this Mac without being paired. Kept across launches.
    @Published private(set) var flyyDropReceiving = FlyyDrop.Receiving.off

    /// When "Everyone for 10 minutes" ends.
    @Published private(set) var flyyDropUntil: Date?

    /// A paired phone is in Bluetooth range but not reachable over the network.
    @Published private(set) var phoneNearby = false
    /// What joining the phone's hotspot is doing, or why it failed.
    @Published private(set) var hotspotStatus: String?

    /// The phone being looked for: its map shows in the panel until closed.
    @Published var finding: String?
    @Published var mirrorsNotifications: Bool {
        didSet {
            notifications.isEnabled = mirrorsNotifications
            UserDefaults.standard.set(mirrorsNotifications, forKey: "mirrorsNotifications")
        }
    }

    /// How the panel switches between several paired phones.
    @Published var switcherStyle: PhoneSwitcherStyle {
        didSet { UserDefaults.standard.set(switcherStyle.rawValue, forKey: "switcherStyle") }
    }

    @Published var opensAtLogin: Bool {
        didSet { LoginItem.set(opensAtLogin) }
    }

    @Published var usesShortcuts: Bool {
        didSet {
            shortcuts.isEnabled = usesShortcuts
            UserDefaults.standard.set(usesShortcuts, forKey: "usesShortcuts")
        }
    }

    /// ⌃⌥ shortcuts for the shown phone that work from any app.
    private(set) lazy var shortcuts = GlobalShortcuts([
        .init(keyCode: kVK_ANSI_A, symbol: "⌃⌥A", title: "Open FlyyLink") { MenuBarPanel.toggle() },
        .init(keyCode: kVK_ANSI_C, symbol: "⌃⌥C", title: "Send clipboard") { [weak self] in
            if let id = self?.primaryId { self?.sendClipboard(to: id) }
        },
        .init(keyCode: kVK_ANSI_F, symbol: "⌃⌥F", title: "Send files") { [weak self] in
            if let id = self?.primaryId { self?.chooseFiles(for: id) }
        },
        .init(keyCode: kVK_ANSI_M, symbol: "⌃⌥M", title: "Mirror phone") { [weak self] in
            if let id = self?.primaryId { self?.mirror(id) }
        },
        .init(keyCode: kVK_ANSI_R, symbol: "⌃⌥R", title: "Ring phone") { [weak self] in
            if let id = self?.primaryId { self?.ring(id) }
        },
    ])

    /// Looks for new versions of FlyyLink.
    let updater = AppUpdater()

    /// Whether this Mac may connect, and the sign-in card.
    let licence = LicenceModel()
    private var licenceChanges: AnyCancellable?

    /// Sends what is shared from the Share menu.
    private(set) lazy var shareInbox = ShareInbox(model: self)

    static let downloads = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
        ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads")

    /// Notification Center only works when FlyyLink runs as a bundled app.
    let notificationsAvailable = NotificationPlugin.isAvailable

    let identity: DeviceIdentity

    private let connected = DeviceRegistry()
    private let plugins: PluginRouter
    private let battery: BatteryPlugin
    private let connectivity = ConnectivityPlugin()
    private let clipboard: ClipboardPlugin
    private let notifications: NotificationPlugin
    private let notificationShare: NotificationSharePlugin
    private let share = SharePlugin(folder: AppModel.downloads)
    private let mediaPlugin = MediaPlugin()
    private let sms = SmsPlugin()
    private let contacts = ContactsPlugin()
    private let locations = LocationPlugin()
    private let photosPlugin = PhotosPlugin()
    private let filesPlugin = FilesPlugin()
    /// Each paired phone's storage in Finder.
    private let drives: PhoneDrives
    private let calls = CallPlugin()
    private let callAudioPlugin = CallAudioPlugin()
    private let find = FindPlugin()
    private let mediaHost: MediaHostPlugin
    private let inputShare: InputSharePlugin
    private let screenHost = ScreenHostPlugin()
    private let relay: PhoneRelayPlugin
    private let flyyDropPlugin: FlyyDropPlugin
    /// Carries call audio over Bluetooth, with this Mac as the phone's hands-free device.
    let callAudio = HandsFreeAudio()
    private let callPanel = CallPanel()
    private var messagesDevice: String?
    private var photosDevice: String?

    let messages = MessagesStore()
    let photos = PhotosStore()
    let playOnTv = PlayOnTvModel()
    let screen: ScreenMirror
    private let screenPlugin: ScreenPlugin
    private let screenWindow: ScreenWindowController
    private var screenSize: AnyCancellable?
    let desktop = RemoteDesktop()
    private let desktopWindow: RemoteDesktopWindowController
    private lazy var remoteWindow = TvRemoteWindowController(model: self)
    private var desktopSize: AnyCancellable?
    private let nearby: NearbyFinder
    private let hotspot = PhoneHotspot()
    private var lastNearbySearch = Date.distantPast
    private var notificationsDay = Calendar.current.startOfDay(for: Date())
    private var link: LanLink?
    private var linking = false
    private var timer: Timer?
    private let trustStore = DefaultsTrustStore()
    private let log = Logger(subsystem: "com.toneflix.flyylink", category: "link")
    /// What the desktop widget last showed. Phones that disconnect stay, dimmed, with their last battery level.
    private var widgetSnapshot = WidgetSnapshot.load()

    /// App Nap would slow the heartbeat timer and make the phone look gone.
    private let activity = ProcessInfo.processInfo.beginActivity(
        options: .userInitiatedAllowingIdleSystemSleep,
        reason: "Keeps the phone connection alive"
    )

    init() {
        // Menu bar only: no Dock icon.
        NSApplication.shared.setActivationPolicy(.accessory)

        identity = DeviceIdentity(
            deviceId: Self.storedDeviceId(),
            deviceName: DeviceIdentity.sanitizeName(Host.current().localizedName ?? "Mac"),
            deviceType: Self.isLaptop() ? .laptop : .desktop
        )
        battery = BatteryPlugin(devices: connected)
        mediaHost = MediaHostPlugin(devices: connected)
        inputShare = InputSharePlugin(devices: connected)
        relay = PhoneRelayPlugin(devices: connected)
        clipboard = ClipboardPlugin(devices: connected)
        nearby = NearbyFinder(ownId: identity.deviceId)
        let screen = ScreenMirror()
        self.screen = screen
        screenPlugin = ScreenPlugin(decoder: screen.decoder, audio: screen.audio, desktop: desktop.feed)
        screenWindow = ScreenWindowController(mirror: screen)
        desktopWindow = RemoteDesktopWindowController(desktop: desktop)
        notifications = NotificationPlugin(devices: connected)
        notificationShare = NotificationSharePlugin(devices: connected)
        flyyDropPlugin = FlyyDropPlugin(share: share, devices: connected)
        drives = PhoneDrives(files: filesPlugin, devices: connected)
        plugins = PluginRouter([notifications, battery, connectivity, clipboard, share, mediaPlugin, sms, contacts, calls, callAudioPlugin, locations, photosPlugin, filesPlugin, screenPlugin, find, mediaHost, inputShare, screenHost, relay, flyyDropPlugin, notificationShare])
        mirrorsNotifications = UserDefaults.standard.object(forKey: "mirrorsNotifications") as? Bool ?? true
        switcherStyle = UserDefaults.standard.string(forKey: "switcherStyle").flatMap(PhoneSwitcherStyle.init(rawValue:)) ?? .carousel
        opensAtLogin = LoginItem.isEnabled
        usesShortcuts = UserDefaults.standard.object(forKey: "usesShortcuts") as? Bool ?? true
        notifications.isEnabled = mirrorsNotifications

        battery.onChange = { [weak self] in Task { @MainActor in self?.publish() } }
        connectivity.onChange = { [weak self] in Task { @MainActor in self?.publish() } }
        notificationShare.onAccessChange = { [weak self] readable in Task { @MainActor in self?.canReadNotifications = readable } }
        notificationShare.start()
        inputShare.onChange = { [weak self] in Task { @MainActor in self?.inputChanged() } }
        let inputShare = inputShare
        screenHost.allows = { [weak inputShare] in inputShare?.allowsControl ?? false }
        notifications.send = { [weak self] id, packet in self?.sendToPhone(id, packet) ?? false }
        let shown = notifications
        relay.onRelayed = { [weak self] message, via in
            let name = "\(message.phoneName) via \(via)"
            if message.packet.type == PacketType.notification {
                shown.presentRelayed(message.packet, phoneId: message.phoneId, phoneName: name)
                return
            }
            guard let state = Call.state(message.packet) else { return }

            let phoneId = message.phoneId
            Task { @MainActor in self?.handleCall(state, deviceId: phoneId, deviceName: name) }
        }
        screenHost.onChange = { [weak self] viewer in Task { @MainActor in self?.screenViewer = viewer } }
        mediaPlugin.onChange = { [weak self] in Task { @MainActor in self?.publish() } }
        locations.onChange = { [weak self] in Task { @MainActor in self?.publish() } }
        sms.onMessages = { [weak self] messages, deviceId in Task { @MainActor in self?.messages.ingest(messages, from: deviceId) } }
        contacts.onContacts = { [weak self] contacts, deviceId in Task { @MainActor in self?.messages.ingest(contacts, from: deviceId) } }
        calls.onState = { [weak self] state, device in
            let deviceId = device.id
            let deviceName = device.name
            Task { @MainActor in self?.handleCall(state, deviceId: deviceId, deviceName: deviceName) }
        }
        callAudioPlugin.onStatus = { [weak self] status, deviceId in Task { @MainActor in self?.callAudio.received(status, from: deviceId) } }
        callAudio.send = { [weak self] packet in
            guard let id = self?.callAudio.phoneId, let device = self?.connected[id] else { return false }
            return device.send(packet)
        }
        nearby.onNearby = { [weak self] found in Task { @MainActor in self?.nearbyChanged(found) } }
        nearby.onAnswer = { [weak self] answer in Task { @MainActor in self?.reach(answer) } }
        screen.send = { [weak self] id, packet in self?.connected[id]?.send(packet) ?? false }
        screenPlugin.onStarted = { [weak self] device, info in
            let deviceId = device.id
            let deviceName = device.name
            Task { @MainActor in
                if info.codec == Screen.jpeg {
                    self?.desktop.started(deviceId: deviceId, name: deviceName, platform: info.platform)
                    self?.desktopWindow.show()
                } else {
                    self?.screen.started(deviceId: deviceId, name: deviceName)
                    self?.screenWindow.show()
                }
            }
        }
        screenPlugin.onEnded = { [weak self] deviceId in
            Task { @MainActor in
                self?.screen.ended(deviceId: deviceId)
                self?.desktop.ended(deviceId: deviceId)
            }
        }
        screenSize = screen.$videoSize.sink { [weak self] size in Task { @MainActor in self?.screenWindow.fit(size) } }
        desktop.send = { [weak self] id, packet in self?.connected[id]?.send(packet) ?? false }
        desktopSize = desktop.$picture
            .map { CGSize(width: $0?.width ?? 0, height: $0?.height ?? 0) }
            .removeDuplicates()
            .sink { [weak self] size in Task { @MainActor in self?.desktopWindow.fit(size) } }
        photosPlugin.onPage = { [weak self] page, deviceId in Task { @MainActor in self?.photos.ingest(page, from: deviceId) } }
        photosPlugin.onPreview = { [weak self] id, url, deviceId in
            Task { @MainActor in self?.photos.ingestPreview(id, url: url, from: deviceId) }
        }
        photos.send = { [weak self] packet in
            guard let id = self?.phoneId, let device = self?.connected[id] else { return false }
            return device.send(packet)
        }
        messages.send = { [weak self] packet in
            guard let id = self?.messagesDevice, let device = self?.connected[id] else { return false }
            return device.send(packet)
        }
        clipboard.onSync = { [weak self] in Task { @MainActor in self?.clipboardSyncedAt = Date() } }
        notifications.onShown = { [weak self] in Task { @MainActor in self?.countNotification() } }
        share.onActivity = { [weak self] activity in Task { @MainActor in self?.record(activity) } }
        share.onFileReceived = { [weak notifications] url, device in notifications?.announce(file: url, from: device) }
        share.restore()
        restoreFlyyDrop()
        licenceChanges = licence.objectWillChange.sink { [weak self] in self?.objectWillChange.send() }
        licence.onLockChange = { [weak self] in self?.licenceChanged() }
        licence.start()
        if !licence.isLocked { start() }
        clipboard.startWatching()
        shortcuts.isEnabled = usesShortcuts
        drives.prune(keeping: Set(trustStore.trustedIds))
        drives.start()
    }

    /// The paired phone after the one shown, wrapping round to the first.
    var nextPhone: DeviceState? {
        DeviceFocus(preferredId: primaryId).next(devices.filter { $0.pairState == .paired }, id: \.id, name: \.name)
    }

    func showNextPhone() {
        guard let next = nextPhone, next.id != primaryId else { return }

        selectedPhone = next.id
    }

    func requestPair(_ id: String) { connected[id]?.requestPair() }

    func accept(_ id: String) { connected[id]?.accept() }

    func reject(_ id: String) { connected[id]?.reject() }

    func unpair(_ id: String) {
        connected[id]?.unpair()
        drives.forget(id)
        widgetSnapshot.phones.removeAll { $0.id == id }
        try? widgetSnapshot.save()
        WidgetCenter.shared.reloadAllTimelines()
    }

    /// Rings the phone and asks where it is, so it can be found by sound or on the map. A computer only plays a sound.
    func ring(_ id: String) {
        guard let device = connected[id] else { return }

        device.send(FindMyPhone.request())
        guard device.link.identity.deviceType.isPhone else { return }

        locations.request(from: device)
        finding = id
    }

    /// Opens the remote for a paired TV.
    func showRemote(_ id: String) {
        guard let device = connected[id], device.isPaired else { return }

        remoteWindow.show(deviceId: id, name: device.name)
    }

    /// Sends a remote press to the TV.
    func press(_ press: TvRemote, on id: String) {
        connected[id]?.send(press.toPacket())
    }

    /// Asks the phone or computer to share its screen and opens the window that shows it.
    func mirror(_ id: String) {
        guard let device = connected[id], device.isPaired else { return }

        if device.link.identity.deviceType.isComputer {
            desktop.begin(deviceId: id, name: device.name)
            desktopWindow.show()
            return
        }

        screen.begin(deviceId: id, name: device.name)
        screenWindow.show()
    }

    /// The computer watching this Mac's screen now, or nil.
    @Published private(set) var screenViewer: String?

    /// Stops showing this Mac's screen to whoever is watching.
    func stopScreenShare() { screenHost.stop() }

    // MARK: Keyboard and mouse

    /// The computer this Mac's keyboard and mouse work now, or nil.
    @Published private(set) var inputTarget: String?

    /// The computer working this Mac now, or nil.
    @Published private(set) var inputController: String?

    /// Bumped when the edge settings change, so views read them again.
    @Published private(set) var inputEdgesVersion = 0

    var inputNeedsAccess: Bool { !inputShare.hasAccess }

    var allowsRemoteInput: Bool {
        get { inputShare.allowsControl }
        set {
            inputShare.allowsControl = newValue
            if !newValue { screenHost.stop() }
        }
    }

    func inputEdge(for id: String) -> ScreenEdge? { inputShare.edge(for: id) }

    func setInputEdge(_ edge: ScreenEdge?, for id: String) { inputShare.setEdge(edge, for: id) }

    func retryInputAccess() { inputShare.retryAccess() }

    private func inputChanged() {
        inputTarget = inputShare.capture.remoteId
        inputController = inputShare.injector.controllerId
        inputEdgesVersion += 1
    }

    func media(_ id: String, _ player: String, _ action: MediaAction) {
        connected[id]?.send(MediaRequest.action(player, action).toPacket())
    }

    func seek(_ id: String, _ player: String, to position: Int64) {
        connected[id]?.send(MediaRequest.seek(player, to: position).toPacket())
    }

    // MARK: Calls

    /// Answers over Bluetooth when call audio is set up, so the call is heard here. Otherwise the phone answers.
    func answerCall() {
        guard let call else { return }
        if callAudio.carries(call.deviceId), callAudio.answer() { return }

        _ = sendToPhone(call.deviceId, Call.answer())
    }

    /// Mutes the microphone the call is using: this Mac's when the audio is here, the phone's otherwise.
    func toggleMute() {
        guard var call else { return }

        call.isMuted.toggle()
        if callAudio.audioOnMac && callAudio.carries(call.deviceId) {
            callAudio.setMuted(call.isMuted)
        } else {
            _ = sendToPhone(call.deviceId, Call.mute(call.isMuted))
        }
        self.call = call
    }

    /// Moves the call's audio between this Mac and the phone.
    func switchCallAudio() {
        callAudio.moveAudio(toMac: !callAudio.audioOnMac)
    }

    func declineCall() {
        guard let call else { return }
        _ = sendToPhone(call.deviceId, Call.decline())
        endCall()
    }

    func hangUp() {
        guard let call else { return }
        _ = sendToPhone(call.deviceId, Call.hangUp())
        endCall()
    }

    /// Places a call on the paired phone.
    func dial(_ number: String, name: String?) {
        let number = number.trimmingCharacters(in: .whitespaces)
        guard !number.isEmpty, let phone = primaryPhone, phone.send(Call.dial(number)) else { return }

        call = ActiveCall(deviceId: phone.id, deviceName: phone.name, number: number, name: name, phase: .dialing)
        callPanel.show(self)
    }

    /// Sends to a phone, directly or through the computer that passed it on.
    private func sendToPhone(_ id: String, _ packet: NetworkPacket) -> Bool {
        if let device = connected[id], device.isPaired { return device.send(packet) }

        return relay.send(packet, toPhone: id)
    }

    /// Whether this Mac's own notifications go to its phones and computers.
    var sharesNotifications: Bool {
        get { notificationShare.isEnabled }
        set {
            notificationShare.isEnabled = newValue
            objectWillChange.send()
        }
    }

    /// False until FlyyLink has Full Disk Access, which reading Notification Center needs.
    @Published private(set) var canReadNotifications = false

        /// Whether this Mac passes its phones' notifications and calls to its other computers, and shows theirs.
    var relaysPhones: Bool {
        get { relay.isEnabled }
        set {
            relay.isEnabled = newValue
            objectWillChange.send()
        }
    }

    private func handleCall(_ state: Call.State, deviceId: String, deviceName: String) {
        if state.ended {
            endCall()
            return
        }

        let photo = state.photo.flatMap { Data(base64Encoded: $0) }.flatMap(NSImage.init(data:))
        switch state.event {
        case .ringing:
            call = ActiveCall(deviceId: deviceId, deviceName: deviceName, number: state.number, name: state.contactName, photo: photo, phase: .ringing)
            callPanel.show(self)
        case .talking:
            var current = call ?? ActiveCall(deviceId: deviceId, deviceName: deviceName, number: state.number, name: state.contactName, photo: photo, phase: .dialing)
            if case .talking = current.phase {} else { current.phase = .talking(since: Date()) }
            call = current
            callPanel.show(self)
        case .missed:
            endCall()
            notifications.announceMissedCall(from: state.contactName ?? state.number, number: state.number, deviceId: deviceId, deviceName: deviceName)
        }
    }

    private func endCall() {
        if call?.isMuted == true {
            callAudio.setMuted(false)
            if let call { _ = sendToPhone(call.deviceId, Call.mute(false)) }
        }

        call = nil
        callPanel.hide()
    }

    func sendClipboard(to id: String) {
        guard let device = connected[id] else { return }

        if !clipboard.send(to: device) {
            record(Activity(.failed, title: "Clipboard is empty", detail: "Copy some text first"))
        }
    }

    /// Opens a file picker and sends what the user picks.
    func chooseFiles(for id: String) {
        NSApplication.shared.activate(ignoringOtherApps: true)

        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.prompt = "Send"
        panel.message = "Choose files to send to \(connected[id]?.name ?? "your phone")"
        panel.begin { [weak self] response in
            guard response == .OK else { return }

            let urls = panel.urls
            Task { @MainActor in self?.sendFiles(urls, to: id) }
        }
    }

    func sendFiles(_ urls: [URL], to id: String) {
        guard let device = connected[id] else { return }

        let share = share
        DispatchQueue.global(qos: .userInitiated).async {
            urls.forEach { share.send($0, to: device) }
        }
    }

    /// Sends a web link to open on the phone, or other text to put on its clipboard.
    func sendText(_ text: String, to id: String) {
        guard let device = connected[id] else { return }

        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let url = URL(string: trimmed).flatMap { ["http", "https"].contains($0.scheme?.lowercased()) ? $0 : nil }
        let packet = url.map { Share.linkPacket($0.absoluteString) } ?? Share.textPacket(trimmed)
        guard device.send(packet) else { return }

        if let url {
            record(Activity(.link(url), title: url.host ?? trimmed, detail: "Opened on \(device.name)"))
        } else {
            record(Activity(.text, title: "“\(trimmed.prefix(60))”", detail: "Copied to \(device.name)"))
        }
    }

    // MARK: FlyyDrop

    func setFlyyDropReceiving(_ mode: FlyyDrop.Receiving) {
        flyyDropPlugin.drop.receiving = mode
        UserDefaults.standard.set(mode.rawValue, forKey: "flyyDropReceiving")
        UserDefaults.standard.set(flyyDropPlugin.drop.receivingUntil, forKey: "flyyDropUntil")
        publishFlyyDropReceiving()
    }

    /// Asks a nearby device to take these files. Its owner sees them and accepts or declines.
    func flyyDrop(_ urls: [URL], to id: String) {
        guard let device = connected[id] else { return }

        let plugin = flyyDropPlugin
        DispatchQueue.global(qos: .userInitiated).async { plugin.send(urls, to: device) }
    }

    /// Opens a file picker and FlyyDrops what the user picks.
    func chooseFilesToFlyyDrop(for id: String) {
        NSApplication.shared.activate(ignoringOtherApps: true)

        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.prompt = "FlyyDrop"
        panel.message = "Choose files to FlyyDrop to \(connected[id]?.name ?? "this device"). They'll be asked to accept."
        panel.begin { [weak self] response in
            guard response == .OK else { return }

            let urls = panel.urls
            Task { @MainActor in self?.flyyDrop(urls, to: id) }
        }
    }

    func cancelFlyyDrop(to deviceId: String) {
        guard let send = flyyDropSends[deviceId] else { return }

        flyyDropPlugin.cancel(send.id, to: deviceId)
        flyyDropSends[deviceId] = nil
    }

    func acceptFlyyDrop(_ id: String) { flyyDropPlugin.accept(id) }

    func declineFlyyDrop(_ id: String) { flyyDropPlugin.decline(id) }

    private func restoreFlyyDrop() {
        let defaults = UserDefaults.standard
        let mode = defaults.string(forKey: "flyyDropReceiving").flatMap(FlyyDrop.Receiving.init(rawValue:)) ?? .off
        flyyDropPlugin.drop.restore(mode, until: (defaults.object(forKey: "flyyDropUntil") as? NSNumber)?.int64Value)
        publishFlyyDropReceiving()

        let notifications = notifications
        flyyDropPlugin.onAsk = { [weak self] ask in
            notifications.announce(ask)
            Task { @MainActor in
                self?.flyyDropAsks.append(ask)
                NSApplication.shared.requestUserAttention(.informationalRequest)
            }
        }
        flyyDropPlugin.onAskEnded = { [weak self] id in
            notifications.withdrawFlyyDrop(id)
            Task { @MainActor in self?.flyyDropAsks.removeAll { $0.id == id } }
        }
        flyyDropPlugin.onSend = { [weak self] deviceId, send in
            Task { @MainActor in
                guard let self else { return }
                guard send.stage == .waiting || self.flyyDropSends[deviceId]?.id == send.id else { return }

                self.flyyDropSends[deviceId] = send
                if send.isFinished { self.forgetFlyyDrop(send, to: deviceId) }
            }
        }
        flyyDropPlugin.onActivity = { [weak self] activity in Task { @MainActor in self?.record(activity) } }
    }

    /// Clears a finished FlyyDrop from its row after a few seconds.
    private func forgetFlyyDrop(_ send: FlyyDropSend, to deviceId: String) {
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 6_000_000_000)
            if flyyDropSends[deviceId]?.id == send.id { flyyDropSends[deviceId] = nil }
        }
    }

    private func tickFlyyDrop() {
        flyyDropPlugin.tick()
        if flyyDropPlugin.drop.receiving != flyyDropReceiving { publishFlyyDropReceiving() }
    }

    private func publishFlyyDropReceiving() {
        flyyDropReceiving = flyyDropPlugin.drop.receiving
        flyyDropUntil = flyyDropPlugin.drop.receivingUntil.map { Date(timeIntervalSince1970: TimeInterval($0) / 1000) }
    }

    func clearRecent() {
        recent.removeAll { !$0.isInProgress }
        share.forgetUnfinished()
    }

    private func record(_ activity: Activity) {
        if let index = recent.firstIndex(where: { $0.id == activity.id }) {
            recent[index] = activity
            return
        }

        recent.insert(activity, at: 0)
        if recent.count > 20 { recent.removeLast(recent.count - 20) }
    }

    private func countNotification() {
        let today = Calendar.current.startOfDay(for: Date())
        if today != notificationsDay {
            notificationsDay = today
            notificationsToday = 0
        }

        notificationsToday += 1
    }

    /// Finds and connects to devices. Runs only while the licence allows it.
    private func start() {
        guard link == nil, !linking else { return }

        linking = true
        let me = identity
        let trustStore = trustStore
        let connected = connected
        let guests = flyyDropPlugin.drop

        Task.detached { [weak self] in
            do {
                // Creating the certificate on first launch is slow, so keep it off the main thread.
                let certificate = try Self.loadCertificate(deviceId: me.deviceId)
                let link = try LanLink(
                    identityProvider: { me },
                    certificate: certificate,
                    trustStore: trustStore,
                    isConnected: { connected[$0]?.link.isOpen == true }
                ) { deviceLink in
                    // Create the device right away on the network thread so no early packet is missed.
                    let device = Device(link: deviceLink, ownCertificate: certificate, trustStore: trustStore, guests: guests)
                    device.delegate = self
                    device.start()
                    Task { @MainActor in self?.add(device) }
                }
                try link.start()

                await self?.started(link, certificate: certificate)
            } catch {
                await self?.failed("Could not start: \(error)")
            }
        }
    }

    private func started(_ link: LanLink, certificate: DeviceCertificate) {
        linking = false
        guard !licence.isLocked else {
            link.stop()
            return
        }

        self.link = link
        nearby.use(privateKeyPEM: certificate.privateKeyPEM)
        let timer = Timer(timeInterval: 5, repeats: true) { [weak self] _ in
            let result = link.broadcastIdentity()
            Task { @MainActor in
                self?.noteBroadcast(result)
                self?.connected.all.forEach { $0.tick() }
                self?.tickFlyyDrop()
                self?.searchNearbyIfLost()
            }
        }
        // Common modes keep it running while the menu is open.
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
        noteBroadcast(link.broadcastIdentity())
    }

    private func licenceChanged() {
        guard licence.isLocked else {
            start()
            return
        }

        timer?.invalidate()
        timer = nil
        link?.stop()
        link = nil
        connected.all.forEach { $0.link.close(reason: "licence locked") }
    }

    private func noteBroadcast(_ result: LanLink.BroadcastResult) {
        let blocked = result == .blocked
        guard blocked != localNetworkBlocked else { return }

        localNetworkBlocked = blocked
        if blocked { log.error("macOS blocked the identity broadcast; Local Network access is off for FlyyLink") }
    }

    func openLocalNetworkSettings() {
        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_LocalNetwork") else { return }

        NSWorkspace.shared.open(url)
    }

    // MARK: Bluetooth fallback

    /// When no paired phone has answered on the network, look for one over Bluetooth now and then.
    private func searchNearbyIfLost() {
        let hasPhone = !pairedPhones.isEmpty
        guard !hasPhone, !trustStore.trustedIds.isEmpty, Date().timeIntervalSince(lastNearbySearch) > 45 else { return }

        lastNearbySearch = Date()
        nearby.search(.connect)
    }

    private func nearbyChanged(_ found: Bool) {
        let hasPhone = !pairedPhones.isEmpty
        phoneNearby = found && !hasPhone
    }

    /// The name of the phone's hotspot, once this Mac has been on it.
    var phoneHotspotName: String? { hotspotPhoneId.flatMap(hotspot.name(of:)) }

    private var hotspotPhoneId: String? {
        let candidates = [selectedPhone].compactMap { $0 } + trustStore.trustedIds
        return candidates.first { hotspot.name(of: $0) != nil }
    }

    /// Asks the phone to turn its hotspot on, then joins it when it shows up. This Mac leaves its current Wi-Fi.
    func joinPhoneHotspot() {
        lastNearbySearch = Date()
        nearby.search(.hotspot)

        guard let id = hotspotPhoneId else {
            hotspotStatus = "Turn on your phone's hotspot and choose it from the Wi-Fi menu once."
            return
        }

        hotspotStatus = "Turning on your phone's hotspot…"
        hotspot.join(deviceId: id) { [weak self] status in Task { @MainActor in self?.hotspotChanged(status) } }
    }

    func cancelHotspot() {
        hotspot.cancel()
        hotspotStatus = nil
    }

    private func hotspotChanged(_ status: PhoneHotspot.Status?) {
        switch status {
        case .waiting(let ssid): hotspotStatus = "Tap the notification on your phone to turn on \(ssid)."
        case .joining(let ssid): hotspotStatus = "Joining \(ssid)…"
        case .failed(let reason): hotspotStatus = reason
        case nil: hotspotStatus = "Connecting…"
        }
    }

    /// Remembers the phone's hotspot when this Mac is on it. Reading the Wi-Fi name needs location access.
    private func learnHotspot(_ device: Device) {
        let deviceId = device.id
        let host = device.link.host
        let hotspot = hotspot
        let nearby = nearby

        DispatchQueue.global(qos: .utility).async {
            if !hotspot.learn(deviceId: deviceId, phoneHost: host) { nearby.askForLocation() }
        }
    }

    /// Connects to the addresses the phone gave over Bluetooth.
    private func reach(_ answer: Nearby.Answer) {
        let peer = DeviceIdentity(deviceId: answer.deviceId, deviceName: "Phone", deviceType: .phone, tcpPort: answer.port)
        guard let link else { return }

        answer.addresses.forEach { _ = link.connect(to: peer, host: $0) }
    }

    private func failed(_ message: String) {
        linking = false
        error = message
    }

    private func add(_ device: Device) {
        if let existing = connected[device.id], existing !== device,
           DeviceLink.keepsExisting(existing.link, over: device.link, ownDeviceId: identity.deviceId) {
            device.link.close(reason: "both sides connected at once; kept the other link")
            return
        }

        if let old = connected.insert(device), old !== device { old.link.close(reason: "replaced by a newer link") }
        log.info("Connected to \(device.name, privacy: .public) (\(device.link.isOutgoing ? "we" : "they", privacy: .public) connected)")
        if device.isPaired {
            plugins.onPaired(device)
        }
        if device.isPaired, device.link.identity.deviceType.isPhone {
            phoneNearby = false
            hotspotStatus = nil
            hotspot.cancel()
            learnHotspot(device)
        }

        publish()
    }

    fileprivate func remove(_ device: Device) {
        guard connected.remove(device) else { return }

        mediaPlugin.forget(device.id)
        inputShare.forget(device.id)
        screenHost.stop(device.id)
        relay.forget(device.id)
        screen.ended(deviceId: device.id)
        desktop.ended(deviceId: device.id)

        log.info("Disconnected from \(device.name, privacy: .public): \(device.link.closeReason ?? "unknown", privacy: .public)")

        publish()
    }

    fileprivate func publish() {
        primaryId = primaryDevice?.id
        phoneId = primaryPhone?.id
        callAudio.attach(callAudioPhoneId)
        syncMessages()

        devices = connected.all
            .map { device in
                let state = device.pairState
                let showsCode = state == .requested || state == .requestedByPeer

                return DeviceState(
                    id: device.id,
                    name: device.name,
                    type: device.link.identity.deviceType,
                    host: device.link.host,
                    pairState: state,
                    code: showsCode ? device.verificationCode : nil,
                    battery: battery.status(for: device.id),
                    signal: connectivity.signal(for: device.id),
                    media: mediaPlugin.nowPlaying(for: device.id),
                    location: locations.location(for: device.id),
                    canFlyyDrop: device.link.identity.incomingCapabilities.contains(PacketType.flyyDropRequest),
                    features: device.features
                )
            }
            .sorted { $0.name < $1.name }

        updateWidget()
        drives.show(pairedPhones.filter(\.sharesFiles).map { (id: $0.id, name: $0.name) })
    }

    /// Opens the phone's storage in Finder.
    func openFiles(_ id: String) {
        drives.open(id)
    }

    func sharesFiles(_ id: String) -> Bool {
        connected[id]?.sharesFiles ?? false
    }

    /// Saves each paired phone's battery for the widget, and asks it to redraw when that changed.
    private func updateWidget() {
        let live = devices.filter { $0.pairState == .paired && ($0.type == .phone || $0.type == .tablet) }
        let liveIds = Set(live.map(\.id))

        var phones = live.map { phone in
            WidgetSnapshot.Phone(
                id: phone.id,
                name: phone.name,
                isTablet: phone.type == .tablet,
                charge: phone.battery?.charge,
                isCharging: phone.battery?.isCharging ?? false,
                isLow: phone.battery?.isLow ?? false
            )
        }
        phones += widgetSnapshot.phones.filter { !liveIds.contains($0.id) }.map { phone in
            var offline = phone
            offline.isConnected = false
            offline.isCharging = false
            return offline
        }
        // The phone the panel shows first, then connected ones, then by name.
        phones.sort { a, b in
            if (a.id == primaryId) != (b.id == primaryId) { return a.id == primaryId }
            if a.isConnected != b.isConnected { return a.isConnected }
            return a.name.localizedStandardCompare(b.name) == .orderedAscending
        }

        let snapshot = WidgetSnapshot(phones: phones)
        guard snapshot != widgetSnapshot else { return }

        widgetSnapshot = snapshot
        do {
            try snapshot.save()
            WidgetCenter.shared.reloadAllTimelines()
        } catch {
            log.error("Could not save the widget's data: \(error.localizedDescription, privacy: .public)")
        }
    }

    private var pairedPhones: [Device] {
        connected.all.filter { $0.isPaired && $0.link.identity.deviceType.isPhone }
    }

    private var primaryDevice: Device? {
        DeviceFocus(preferredId: selectedPhone).pick(connected.all.filter(\.isPaired), id: \.id, name: \.name)
    }

    /// The phone picked in the panel, or else the first paired phone.
    private var primaryPhone: Device? {
        DeviceFocus(preferredId: selectedPhone).pick(pairedPhones, id: \.id, name: \.name)
    }

    /// Call audio stays with its phone while that phone is connected, so switching phones keeps the Bluetooth link.
    private var callAudioPhoneId: String? {
        if let id = callAudio.phoneId, connected[id]?.isPaired == true { return id }

        return phoneId
    }

    /// Texts and photos come from the primary phone, while it has those features on.
    private func syncMessages() {
        let photosPhone = primaryPhone.flatMap { $0.features.contains(.photos) ? $0 : nil }
        let messagesPhone = primaryPhone.flatMap { $0.features.contains(.messages) ? $0 : nil }

        if let phone = photosPhone {
            photos.attach(phone.id)
            photosDevice = phone.id
        } else if let id = photosDevice {
            photos.detach(id)
        }

        if let phone = messagesPhone, messagesDevice != phone.id || !messages.hasPhone {
            messagesDevice = phone.id
            messages.attach(phone.id)
        } else if messagesPhone == nil, let id = messagesDevice {
            messages.detach(id)
        }
    }

    // MARK: Storage

    private static func storedDeviceId() -> String {
        let defaults = UserDefaults.standard
        if let id = defaults.string(forKey: "deviceId") { return id }

        let id = DeviceIdentity.newDeviceId()
        defaults.set(id, forKey: "deviceId")

        return id
    }

    private nonisolated static func loadCertificate(deviceId: String) throws -> DeviceCertificate {
        let files = FileManager.default
        let folder = try files
            .url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
            .appendingPathComponent("FlyyLink", isDirectory: true)
        try files.createDirectory(at: folder, withIntermediateDirectories: true)

        let certURL = folder.appendingPathComponent("device.crt")
        let keyURL = folder.appendingPathComponent("device.key")

        // Reuse the stored certificate only if it belongs to this device id. Peers refuse
        // a certificate whose common name differs from the id we announce.
        if let der = try? Data(contentsOf: certURL),
           let pem = try? String(contentsOf: keyURL, encoding: .utf8),
           let stored = try? DeviceCertificate(certificateDER: Array(der), privateKeyPEM: pem),
           stored.deviceId == deviceId {
            return stored
        }

        let certificate = try DeviceCertificate.generate(deviceId: deviceId)
        try Data(certificate.certificateDER).write(to: certURL)
        try certificate.privateKeyPEM.write(to: keyURL, atomically: true, encoding: .utf8)
        try files.setAttributes([.posixPermissions: 0o600], ofItemAtPath: keyURL.path)

        return certificate
    }

    private static func isLaptop() -> Bool {
        var size = 0
        sysctlbyname("hw.model", nil, &size, nil, 0)
        guard size > 0 else { return false }

        var model = [CChar](repeating: 0, count: size)
        sysctlbyname("hw.model", &model, &size, nil, 0)

        return String(cString: model).contains("Book")
    }
}

extension AppModel: DeviceDelegate {
    nonisolated func devicePairRequested(_ device: Device, code: String) {
        Task { @MainActor in
            self.publish()
            NSApplication.shared.requestUserAttention(.informationalRequest)
        }
    }

    nonisolated func device(_ device: Device, pairStateChanged state: PairState) {
        if state == .paired { plugins.onPaired(device) }

        Task { @MainActor in self.publish() }
    }

    nonisolated func device(_ device: Device, received packet: NetworkPacket) {
        if packet.type == PacketType.features {
            plugins.onFeaturesChanged(device)
            Task { @MainActor in self.publish() }
            return
        }

        plugins.dispatch(packet, from: device)
    }

    nonisolated func deviceDisconnected(_ device: Device) {
        Task { @MainActor in self.remove(device) }
    }
}

private extension Device {
    /// Runs a FlyyLink that lets computers browse its storage, with the feature on.
    var sharesFiles: Bool { link.identity.incomingCapabilities.contains(PacketType.filesRequest) && features.contains(.files) }
}
