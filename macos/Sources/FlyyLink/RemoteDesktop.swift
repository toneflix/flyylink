import AppKit
import ImageIO
import FlyyLinkKit

/// Turns the JPEG pictures from another computer into images. Pictures arrive on a network thread; while one is
/// still being shown, newer ones replace the waiting one, so a slow Mac skips pictures instead of falling behind.
final class RemoteDesktopFeed: @unchecked Sendable {
    /// Called on the feed's own queue with each picture to show.
    var onPicture: ((CGImage) -> Void)?

    /// Decodes here rather than later on the main thread when drawn.
    private static let decodeNow = [kCGImageSourceShouldCacheImmediately: true] as CFDictionary

    private let queue = DispatchQueue(label: "com.toneflix.flyylink.remote-desktop")
    private let lock = NSLock()
    private var waiting: Data?
    private var busy = false

    func push(_ jpeg: Data) {
        let start = lock.withLock { () -> Bool in
            waiting = jpeg
            defer { busy = true }
            return !busy
        }
        if start { queue.async { self.drain() } }
    }

    private func drain() {
        while let jpeg = lock.withLock({ () -> Data? in
            defer { waiting = nil }
            if waiting == nil { busy = false }
            return waiting
        }) {
            guard let source = CGImageSourceCreateWithData(jpeg as CFData, nil),
                  let image = CGImageSourceCreateImageAtIndex(source, 0, Self.decodeNow) else { continue }

            onPicture?(image)
        }
    }
}

/// Watching and working another computer's screen. Use on the main thread.
@MainActor
final class RemoteDesktop: ObservableObject {
    enum Phase: Equatable {
        case idle
        case waiting
        case live
        case ended
    }

    /// The longer side of this Mac's screen in pixels, so the picture is never sent larger than it can be shown.
    static var maxSize: Int64 {
        guard let screen = NSScreen.main else { return 1920 }

        let side = max(screen.frame.width, screen.frame.height) * screen.backingScaleFactor
        return min(max(Int64(side), 1280), 3840)
    }

    @Published private(set) var phase = Phase.idle
    @Published private(set) var deviceName = ""
    @Published private(set) var picture: CGImage?

    /// Sends a packet to a device. Set by the app model.
    var send: ((String, NetworkPacket) -> Bool)?

    let feed = RemoteDesktopFeed()

    private(set) var deviceId: String?

    /// The other computer's platform, so Control and Command can be swapped when they differ.
    private(set) var platform: String?

    init() {
        feed.onPicture = { [weak self] image in
            Task { @MainActor in
                guard let self, self.phase == .live else { return }
                self.picture = image
            }
        }
    }

    /// Whether the keys Control and Command trade places on the way, so shortcuts feel the same on both sides.
    var swapsControlAndMeta: Bool { platform == RemoteInput.windows }

    func begin(deviceId: String, name: String) {
        if phase == .live, self.deviceId == deviceId { return }

        if let old = self.deviceId, old != deviceId { leave(old) }
        self.deviceId = deviceId
        deviceName = name
        picture = nil
        phase = send?(deviceId, Screen.start(maxSize: Self.maxSize)) == true ? .waiting : .ended
    }

    func stop() {
        guard let deviceId else { return }

        leave(deviceId)
        self.deviceId = nil
        phase = .idle
        picture = nil
    }

    func input(_ input: RemoteInput) {
        guard phase == .live, let deviceId else { return }

        _ = send?(deviceId, input.toPacket())
    }

    func started(deviceId: String, name: String, platform: String?) {
        if let old = self.deviceId, old != deviceId { leave(old) }

        self.deviceId = deviceId
        self.platform = platform
        deviceName = name
        phase = .live
    }

    func ended(deviceId: String) {
        guard deviceId == self.deviceId, phase != .idle else { return }

        phase = .ended
    }

    /// Stops the stream and lets go of the other computer's keyboard and mouse.
    private func leave(_ deviceId: String) {
        _ = send?(deviceId, RemoteInput.release.toPacket())
        _ = send?(deviceId, Screen.stop())
    }
}
