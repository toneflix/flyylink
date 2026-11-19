import AppKit
import SwiftUI
import FlyyLinkKit

/// The pairing prompt on its own, for the window that pops up when a device asks to pair.
struct PairingWindow: View {
    @ObservedObject var model: AppModel
    let deviceId: String

    var body: some View {
        VStack {
            if let device = model.devices.first(where: { $0.id == deviceId }) {
                PairingPrompt(device: device, model: model)
            }
        }
        .padding(.horizontal, 24)
        .padding(.top, 30)
        .padding(.bottom, 24)
        .frame(width: 340)
        .glassBackdrop()
    }
}

/// Brings a pairing request to the front, above other apps and on every Space, so it is seen without opening the menu.
@MainActor
final class PairingWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private var deviceId: String?
    private var dismissedId: String?
    private weak var model: AppModel?

    init(model: AppModel) {
        self.model = model
    }

    /// Shows the device asking to pair, and closes once no device is asking.
    func update(_ devices: [DeviceState]) {
        let asking = devices.filter { $0.pairState == .requestedByPeer }

        if let dismissedId, !asking.contains(where: { $0.id == dismissedId }) {
            self.dismissedId = nil
        }

        let next = asking.first { $0.id == deviceId } ?? asking.first { $0.id != dismissedId }
        guard let next else { return close() }

        if next.id != deviceId || window?.isVisible != true {
            show(next.id)
        }
    }

    private func show(_ id: String) {
        guard let model else { return }

        let window = window ?? makeWindow()
        let hosting = NSHostingView(rootView: PairingWindow(model: model, deviceId: id))
        hosting.sizingOptions = [.intrinsicContentSize]
        window.contentView = hosting
        window.center()

        deviceId = id
        self.window = window
        NSApplication.shared.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    private func close() {
        deviceId = nil
        window?.delegate = nil
        window?.close()
        window?.delegate = self
    }

    private func makeWindow() -> NSWindow {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 360),
            styleMask: [.titled, .closable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        window.title = "Pair"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        window.isMovableByWindowBackground = true
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.isReleasedWhenClosed = false
        window.delegate = self

        return window
    }

    /// Closing the window leaves the request in the menu bar panel.
    func windowWillClose(_ notification: Notification) {
        dismissedId = deviceId
        deviceId = nil
    }
}
