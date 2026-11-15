import AppKit
import SwiftUI
import FlyyLinkKit

/// Another computer's screen. The pointer, clicks, scrolling and typing go to that computer.
struct RemoteDesktopWindow: View {
    @ObservedObject var desktop: RemoteDesktop

    var body: some View {
        ZStack {
            DesktopSurface(desktop: desktop)

            if desktop.phase != .live || desktop.picture == nil {
                status
            }
        }
        .background(Color.black)
        .preferredColorScheme(.dark)
    }

    private var status: some View {
        VStack(spacing: 12) {
            Image(lucide: desktop.phase == .ended ? .unplug : .screenShare, size: 40, weight: .light)
                .foregroundStyle(Theme.accent)

            Text(title).font(.system(size: 15, weight: .semibold))
            Text(hint)
                .font(.system(size: 12))
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 300)

            if desktop.phase == .waiting || desktop.phase == .live { ProgressView().controlSize(.small) }
            if desktop.phase == .ended, let id = desktop.deviceId {
                Button("Try again") { desktop.begin(deviceId: id, name: desktop.deviceName) }
                    .buttonStyle(.borderedProminent)
            }
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.black)
    }

    private var title: String {
        switch desktop.phase {
        case .ended: return "Screen sharing stopped"
        case .idle: return "Not connected"
        default: return "Connecting to \(desktop.deviceName)"
        }
    }

    private var hint: String {
        switch desktop.phase {
        case .ended:
            return "\(desktop.deviceName) stopped sharing. It needs Screen Recording access and \"Let my devices control this Mac\" on."
        case .idle: return "Start from View screen in the FlyyLink menu."
        default: return "Connecting…"
        }
    }
}

/// Shows the pictures and turns this Mac's pointer and keys into input for the other computer.
private struct DesktopSurface: NSViewRepresentable {
    @ObservedObject var desktop: RemoteDesktop

    func makeNSView(context: Context) -> DesktopInputView {
        let view = DesktopInputView()
        view.send = { [weak desktop] input in desktop?.input(input) }
        view.swapsControlAndMeta = { [weak desktop] in desktop?.swapsControlAndMeta ?? false }
        return view
    }

    func updateNSView(_ view: DesktopInputView, context: Context) {
        view.picture = desktop.picture
    }
}

/// Draws the other computer's picture and sends what the pointer and keys do over it.
final class DesktopInputView: NSView {
    var send: @MainActor (RemoteInput) -> Void = { _ in }
    var swapsControlAndMeta: @MainActor () -> Bool = { false }

    var picture: CGImage? {
        didSet { layer?.contents = picture }
    }

    private var keys: Set<String> = []
    private var buttons: Set<RemoteInput.MouseButton> = []
    private var tracking: NSTrackingArea?

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        layer?.contentsGravity = .resizeAspect
    }

    required init?(coder: NSCoder) { nil }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    var pictureSize: CGSize {
        guard let picture else { return .zero }
        return CGSize(width: picture.width, height: picture.height)
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        window?.makeFirstResponder(self)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }

        let area = NSTrackingArea(rect: .zero, options: [.mouseMoved, .activeInKeyWindow, .inVisibleRect], owner: self)
        addTrackingArea(area)
        tracking = area
    }

    override func resignFirstResponder() -> Bool {
        releaseAll()
        return super.resignFirstResponder()
    }

    /// Lets go of everything held when the window loses focus, so no key stays down over there.
    func releaseAll() {
        keys.forEach { send(.key(code: $0, down: false)) }
        buttons.forEach { send(.button($0, down: false)) }
        keys = []
        buttons = []
    }

    // MARK: Pointer

    override func mouseMoved(with event: NSEvent) { point(event) }
    override func mouseDragged(with event: NSEvent) { point(event) }
    override func rightMouseDragged(with event: NSEvent) { point(event) }
    override func otherMouseDragged(with event: NSEvent) { point(event) }

    override func mouseDown(with event: NSEvent) { press(.left, event, down: true) }
    override func mouseUp(with event: NSEvent) { press(.left, event, down: false) }
    override func rightMouseDown(with event: NSEvent) { press(.right, event, down: true) }
    override func rightMouseUp(with event: NSEvent) { press(.right, event, down: false) }
    override func otherMouseDown(with event: NSEvent) { press(.middle, event, down: true) }
    override func otherMouseUp(with event: NSEvent) { press(.middle, event, down: false) }

    override func scrollWheel(with event: NSEvent) {
        // A mouse wheel reports lines; a trackpad reports points. One line is about one notch.
        let scale = event.hasPreciseScrollingDeltas ? 1 : 30.0
        send(.scroll(dx: -event.scrollingDeltaX * scale, dy: event.scrollingDeltaY * scale))
    }

    private func point(_ event: NSEvent) {
        guard let fraction = fraction(of: event) else { return }

        send(.moveTo(x: fraction.x, y: fraction.y))
    }

    private func press(_ button: RemoteInput.MouseButton, _ event: NSEvent, down: Bool) {
        window?.makeFirstResponder(self)
        point(event)

        if down { buttons.insert(button) } else { buttons.remove(button) }
        send(.button(button, down: down))
    }

    /// Where the event is on the picture, from 0 to 1 from the top left.
    private func fraction(of event: NSEvent) -> CGPoint? {
        let rect = pictureRect()
        guard rect.width > 0, rect.height > 0 else { return nil }

        let location = convert(event.locationInWindow, from: nil)
        return CGPoint(
            x: min(max((location.x - rect.minX) / rect.width, 0), 1),
            y: min(max((location.y - rect.minY) / rect.height, 0), 1)
        )
    }

    /// The picture keeps its shape, so it may not fill the view.
    private func pictureRect() -> CGRect {
        let size = pictureSize
        guard size.width > 0, size.height > 0 else { return .zero }

        let fit = min(bounds.width / size.width, bounds.height / size.height)
        let width = size.width * fit
        let height = size.height * fit
        return CGRect(x: (bounds.width - width) / 2, y: (bounds.height - height) / 2, width: width, height: height)
    }

    // MARK: Keys

    override func keyDown(with event: NSEvent) { key(event, down: true) }
    override func keyUp(with event: NSEvent) { key(event, down: false) }

    /// Shortcuts such as Command-W go to the other computer too, rather than to FlyyLink's menus.
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard window?.firstResponder === self, event.type == .keyDown else { return false }

        key(event, down: true)
        return true
    }

    override func flagsChanged(with event: NSEvent) {
        guard let name = MacKeys.name(Int64(event.keyCode)), let flag = MacKeys.flag(for: name) else { return }

        let down = CGEventFlags(rawValue: UInt64(event.modifierFlags.rawValue)).contains(flag)
        sendKey(name, down: down)
    }

    private func key(_ event: NSEvent, down: Bool) {
        guard let name = MacKeys.name(Int64(event.keyCode)) else { return }

        sendKey(name, down: down)

        // macOS doesn't report a key's release while Command is held, so let it go straight away.
        if down, event.modifierFlags.contains(.command) { sendKey(name, down: false) }
    }

    private func sendKey(_ name: String, down: Bool) {
        let code = swapsControlAndMeta() ? KeyCodes.swappingControlAndMeta(name) : name
        guard down || keys.contains(code) else { return }

        if down { keys.insert(code) } else { keys.remove(code) }
        send(.key(code: code, down: down))
    }
}

/// The window that shows another computer's screen, keeping its shape while resized.
@MainActor
final class RemoteDesktopWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private let desktop: RemoteDesktop

    init(desktop: RemoteDesktop) {
        self.desktop = desktop
    }

    func show() {
        NSApplication.shared.activate(ignoringOtherApps: true)

        if let window {
            window.title = "\(desktop.deviceName)'s screen"
            window.makeKeyAndOrderFront(nil)
            return
        }

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1100, height: 700),
            styleMask: [.titled, .closable, .resizable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "\(desktop.deviceName)'s screen"
        window.isReleasedWhenClosed = false
        window.backgroundColor = .black
        window.contentView = NSHostingView(rootView: RemoteDesktopWindow(desktop: desktop))
        window.delegate = self
        window.center()
        window.setFrameAutosaveName("RemoteDesktop")
        window.makeKeyAndOrderFront(nil)
        self.window = window
    }

    /// Keeps the window the shape of the picture, keeping its width.
    func fit(_ size: CGSize) {
        guard let window, size.width > 0, size.height > 0 else { return }

        window.contentAspectRatio = size
        let content = window.contentRect(forFrameRect: window.frame)
        let height = (content.width * size.height / size.width).rounded()
        let frame = window.frameRect(forContentRect: NSRect(x: content.minX, y: content.maxY - height, width: content.width, height: height))
        window.setFrame(frame, display: true, animate: true)
    }

    func windowDidResignKey(_ notification: Notification) {
        (window?.firstResponder as? DesktopInputView)?.releaseAll()
    }

    func windowWillClose(_ notification: Notification) {
        desktop.stop()
        window = nil
    }
}
