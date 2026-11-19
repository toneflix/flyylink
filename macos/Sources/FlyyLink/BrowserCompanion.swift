import Foundation
import FlyyLinkKit

/// The app's side of the browser add-on: tells browsers which devices and TVs there are, carries out what they
/// ask, hands them sign-in codes from the phone, and lists what their tabs play as players paired devices can control.
/// Mirrors BrowserCompanion.cs.
final class BrowserCompanion: @unchecked Sendable {
    /// What the browsers' tabs play, for `MediaHostPlugin`.
    let media = BrowserMedia()

    /// Called on any thread when a browser's players change.
    var mediaChanged: (() -> Void)?

    /// Builds what the add-on shows.
    var state: (@MainActor () -> BrowserState)?

    /// A paired phone by id, or the shown one when the id is nil.
    var phone: (@MainActor (String?) -> Device?)?

    /// Places a call from a phone, or the shown one when the id is nil.
    var dial: (@MainActor (String, String?) -> Void)?

    /// Plays a web address on a TV found on the network. Returns false when that TV is gone.
    var playOnTv: (@MainActor (_ tv: String, _ url: String, _ title: String, _ mime: String?) -> Bool)?

    private let bridge = BrowserBridge()
    private let devices: DeviceRegistry
    private let share: SharePlugin
    private let downloads = FileManager.default.temporaryDirectory.appendingPathComponent("FlyyLink From browser", isDirectory: true)
    private var lastState: String?

    init(devices: DeviceRegistry, share: SharePlugin) {
        self.devices = devices
        self.share = share

        bridge.onConnected = { [weak self] connection in Task { @MainActor in self?.connected(connection) } }
        bridge.onRequest = { [weak self] connection, request in Task { @MainActor in self?.handle(request, from: connection) } }
        bridge.onClosed = { [weak self] connection in
            self?.media.forget(browser: connection.key)
            self?.mediaChanged?()
        }
    }

    func start() {
        BrowserHosts.register()
        bridge.start()
    }

    func stop() {
        bridge.stop()
    }

    /// Sends the latest state to every browser when it changed.
    @MainActor
    func refresh() {
        guard let state = self.state?() else { return }

        let line = BrowserEvents.state(state)
        guard line != lastState else { return }

        lastState = line
        bridge.broadcast(line)
    }

    /// Passes on the code in a phone notification that just arrived.
    func notified(_ note: PhoneNotification, deviceName: String) {
        guard !note.silent, let code = OneTimeCode.find(title: note.title, text: note.text) else { return }

        bridge.broadcast(BrowserEvents.code(code, app: note.appName, device: deviceName))
    }

    func callChanged(active: Bool) {
        bridge.broadcast(BrowserEvents.call(active: active))
    }

    /// Presses a button on a browser player that a paired device controls. Returns false when the name isn't a browser's.
    func control(_ request: MediaRequest) -> Bool {
        guard let name = request.player, let target = media.find(name) else { return false }

        bridge.connections.first { $0.key == target.browser }?
            .send(BrowserEvents.control(player: target.playerId, action: request.action, seekTo: request.seekTo))
        return true
    }

    @MainActor
    private func connected(_ connection: BrowserConnection) {
        if let state = self.state?() { connection.send(BrowserEvents.state(state)) }
    }

    @MainActor
    private func handle(_ request: BrowserRequest, from connection: BrowserConnection) {
        switch request.kind {
        case .hello(let browser):
            connection.name = browser
        case .media(let players):
            media.update(browser: connection.key, browserName: connection.name, players: players)
            mediaChanged?()
        case .share(let device, let item):
            send(item, to: device, for: request, from: connection)
        case .call(let device, let number):
            dial?(number, device)
            answer(request, on: connection, ok: phone?(device) != nil, error: "offline")
        case .text(let device, let number, let body):
            let sent = phone?(device)?.send(Sms.send(to: [number], body: body)) ?? false
            answer(request, on: connection, ok: sent, error: "offline")
        case .playOnTv(let tv, let url, let title, let mime):
            answer(request, on: connection, ok: playOnTv?(tv, url, title, mime) ?? false, error: "tvGone")
        }
    }

    @MainActor
    private func send(_ item: BrowserShare, to id: String, for request: BrowserRequest, from connection: BrowserConnection) {
        guard let device = devices[id], device.isPaired else {
            answer(request, on: connection, ok: false, error: "offline")
            return
        }

        switch item {
        case .link(let url):
            answer(request, on: connection, ok: device.send(Share.linkPacket(url)), error: "offline")
        case .text(let text):
            answer(request, on: connection, ok: device.send(Share.textPacket(text)), error: "offline")
        case .file(let path):
            let url = URL(fileURLWithPath: path)
            guard FileManager.default.fileExists(atPath: path) else {
                answer(request, on: connection, ok: false, error: "missing")
                return
            }

            let share = share
            DispatchQueue.global(qos: .userInitiated).async { share.send(url, to: device) }
            answer(request, on: connection, ok: true)
        case .fetch(let url, let name):
            fetch(url, name: name, to: device, for: request, from: connection)
        }
    }

    /// Downloads a picture, video or file from a page, then sends it like any other file.
    private func fetch(_ address: String, name: String?, to device: Device, for request: BrowserRequest, from connection: BrowserConnection) {
        guard let url = URL(string: address) else { return }

        let share = share
        let folder = downloads.appendingPathComponent(UUID().uuidString.prefix(8).description, isDirectory: true)
        URLSession.shared.downloadTask(with: url) { [weak self] location, response, error in
            let suggested = name ?? response?.suggestedFilename ?? url.lastPathComponent
            let fileName = (suggested as NSString).lastPathComponent.isEmpty ? "download" : (suggested as NSString).lastPathComponent
            let target = folder.appendingPathComponent(fileName)
            let status = (response as? HTTPURLResponse)?.statusCode ?? 200

            guard let location, error == nil, (200..<300).contains(status),
                  (try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)) != nil,
                  (try? FileManager.default.moveItem(at: location, to: target)) != nil
            else {
                self?.answer(request, on: connection, ok: false, error: "download")
                return
            }

            share.send(target, to: device)
            self?.answer(request, on: connection, ok: true)
        }.resume()
    }

    private func answer(_ request: BrowserRequest, on connection: BrowserConnection, ok: Bool, error: String? = nil) {
        guard let id = request.id else { return }

        connection.send(BrowserEvents.result(id: id, ok: ok, error: ok ? nil : error))
    }
}
