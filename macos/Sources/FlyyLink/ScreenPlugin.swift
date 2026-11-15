import AppKit
import SwiftUI
import FlyyLinkKit

/// Receives screen streams ("flyylink.screen"). A phone's H.264 pictures go to the decoder and its sound to the
/// player; another computer's JPEG pictures go to the remote desktop feed.
final class ScreenPlugin: Plugin {
    let incoming: Set<String> = [PacketType.screen]

    let decoder: ScreenDecoder
    let audio: ScreenAudio
    let desktop: RemoteDesktopFeed

    /// Called on a network thread when a device starts or stops streaming.
    var onStarted: ((Device, Screen.Info) -> Void)?
    var onEnded: ((String) -> Void)?

    init(decoder: ScreenDecoder, audio: ScreenAudio, desktop: RemoteDesktopFeed) {
        self.decoder = decoder
        self.audio = audio
        self.desktop = desktop
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        guard let info = Screen.info(packet) else { return }

        guard info.running, packet.payloadSize == Screen.stream else {
            if !device.link.identity.deviceType.isComputer { audio.stop() }
            onEnded?(device.id)
            return
        }

        if info.codec == Screen.jpeg {
            receivePictures(packet, info: info, from: device)
            return
        }

        decoder.reset()
        if info.hasAudio {
            audio.start(rate: info.audioRate, channels: info.audioChannels)
        } else {
            audio.stop()
        }

        let reader = ScreenFrameReader()
        let decoder = decoder
        let audio = audio
        let deviceId = device.id

        do {
            try device.receiveStream(packet) { data in
                for frame in try reader.feed(data) {
                    if frame.isAudio {
                        audio.play(frame)
                    } else {
                        decoder.decode(frame)
                    }
                }
            }
            .whenComplete { [weak self] _ in
                audio.stop()
                self?.onEnded?(deviceId)
            }

            onStarted?(device, info)
        } catch {
            onEnded?(deviceId)
        }
    }

    private func receivePictures(_ packet: NetworkPacket, info: Screen.Info, from device: Device) {
        let reader = ScreenFrameReader()
        let desktop = desktop
        let deviceId = device.id

        do {
            try device.receiveStream(packet) { data in
                for frame in try reader.feed(data) where !frame.isAudio {
                    desktop.push(frame.data)
                }
            }
            .whenComplete { [weak self] _ in self?.onEnded?(deviceId) }

            onStarted?(device, info)
        } catch {
            onEnded?(deviceId)
        }
    }
}

/// The state of mirroring, for the screen window.
@MainActor
final class ScreenMirror: ObservableObject {
    enum Phase: Equatable {
        case idle
        case waiting
        case live
        case ended
    }

    @Published private(set) var phase = Phase.idle
    @Published private(set) var videoSize = CGSize.zero
    @Published private(set) var deviceName = ""

    let decoder = ScreenDecoder()
    let audio = ScreenAudio()

    /// Silences the phone's sound on the Mac. Remembered between sessions.
    @Published var isMuted = UserDefaults.standard.bool(forKey: "mirrorMuted") {
        didSet {
            audio.isMuted = isMuted
            UserDefaults.standard.set(isMuted, forKey: "mirrorMuted")
        }
    }

    /// Sends a packet to a phone. Set by the app model.
    var send: ((String, NetworkPacket) -> Bool)?

    private(set) var deviceId: String?

    /// The Mac decodes larger pictures than Windows, so it asks for a full-size phone screen.
    static let maxSize: Int64 = 2560

    init() {
        decoder.onSize = { [weak self] size in Task { @MainActor in self?.videoSize = size } }
        audio.isMuted = isMuted
    }

    /// Asks the phone to start; it shows a prompt the user has to allow.
    func begin(deviceId: String, name: String) {
        if phase == .live, self.deviceId == deviceId { return }

        if let old = self.deviceId, old != deviceId { _ = send?(old, Screen.stop()) }
        self.deviceId = deviceId
        deviceName = name
        phase = send?(deviceId, Screen.start(maxSize: Self.maxSize, audio: true)) == true ? .waiting : .ended
    }

    func stop() {
        guard let deviceId else { return }

        _ = send?(deviceId, Screen.stop())
        self.deviceId = nil
        phase = .idle
        decoder.reset()
        audio.stop()
    }

    func input(_ input: ScreenInput) {
        guard phase == .live, let deviceId else { return }

        _ = send?(deviceId, input.toPacket())
    }

    /// A stream arrived, whether the Mac asked for it or the user started it on the phone.
    func started(deviceId: String, name: String) {
        if let old = self.deviceId, old != deviceId { _ = send?(old, Screen.stop()) }

        self.deviceId = deviceId
        deviceName = name
        phase = .live
    }

    func ended(deviceId: String) {
        guard deviceId == self.deviceId, phase != .idle else { return }

        phase = .ended
    }
}

/// The window that shows the phone's screen. An AppKit window, so it can open when the phone starts
/// mirroring by itself and keep the phone's shape while resized.
@MainActor
final class ScreenWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private let mirror: ScreenMirror

    init(mirror: ScreenMirror) {
        self.mirror = mirror
    }

    func show() {
        NSApplication.shared.activate(ignoringOtherApps: true)

        if let window {
            window.makeKeyAndOrderFront(nil)
            return
        }

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 400, height: 820),
            styleMask: [.titled, .closable, .resizable, .miniaturizable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        window.title = "Phone screen"
        window.titlebarAppearsTransparent = true
        window.isReleasedWhenClosed = false
        window.backgroundColor = .black
        window.contentView = NSHostingView(rootView: ScreenWindow(mirror: mirror))
        window.delegate = self
        window.center()
        window.setFrameAutosaveName("PhoneScreen")
        window.makeKeyAndOrderFront(nil)
        self.window = window
    }

    /// Keeps the window the shape of the phone's picture.
    func fit(_ size: CGSize) {
        guard let window, size.width > 0, size.height > 0 else { return }

        window.contentAspectRatio = size
        var frame = window.frame
        let height = frame.height
        frame.size.width = (height * size.width / size.height).rounded()
        window.setFrame(frame, display: true, animate: true)
    }

    func windowWillClose(_ notification: Notification) {
        mirror.stop()
        window = nil
    }
}
