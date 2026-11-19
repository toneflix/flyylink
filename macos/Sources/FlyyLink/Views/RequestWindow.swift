import AppKit
import SwiftUI
import FlyyLinkKit

/// Something waiting for the user's answer: a device asking to pair, or a FlyyDrop asking to be accepted.
enum Request: Hashable {
    case pairing(deviceId: String)
    case flyyDrop(askId: String)

    var title: String {
        switch self {
        case .pairing: return "Pair"
        case .flyyDrop: return "FlyyDrop"
        }
    }
}

/// One request on its own, for the window that pops up when it arrives.
struct RequestWindow: View {
    @ObservedObject var model: AppModel
    let request: Request

    var body: some View {
        VStack {
            switch request {
            case .pairing(let deviceId):
                if let device = model.devices.first(where: { $0.id == deviceId }) {
                    PairingPrompt(device: device, model: model)
                }
            case .flyyDrop(let askId):
                if let ask = model.flyyDropAsks.first(where: { $0.id == askId }) {
                    FlyyDropPrompt(ask: ask, model: model)
                }
            }
        }
        .padding(.horizontal, 24)
        .padding(.top, 30)
        .padding(.bottom, 24)
        .frame(width: 340)
        .glassBackdrop()
    }
}

/// Brings requests to the front one at a time, above other apps and on every Space, so they are seen without opening the menu.
@MainActor
final class RequestWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private var shown: Request?
    private var dismissed: Set<Request> = []
    private weak var model: AppModel?

    init(model: AppModel) {
        self.model = model
    }

    /// Keeps showing the current request while it is pending, else shows the next one, and closes once none is left.
    func update(_ pending: [Request]) {
        dismissed.formIntersection(pending)

        let next = pending.first { $0 == shown } ?? pending.first { !dismissed.contains($0) }
        guard let next else { return close() }

        if next != shown || window?.isVisible != true {
            show(next)
        }
    }

    private func show(_ request: Request) {
        guard let model else { return }

        let window = window ?? makeWindow()
        let hosting = NSHostingView(rootView: RequestWindow(model: model, request: request))
        hosting.sizingOptions = [.intrinsicContentSize]
        window.contentView = hosting
        window.title = request.title
        window.center()

        shown = request
        self.window = window
        NSApplication.shared.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    private func close() {
        shown = nil
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
        if let shown { dismissed.insert(shown) }
        shown = nil
    }
}
