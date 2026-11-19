import AppKit
import SwiftUI
import FlyyLinkKit

/// A remote for a paired TV: the D-pad, Back, Home and volume, a touchpad for the TV's pointer,
/// and the keyboard's keys typed on the TV.
struct TvRemoteWindow: View {
    @ObservedObject var model: AppModel
    let deviceId: String

    private var device: DeviceState? { model.devices.first { $0.id == deviceId && $0.pairState == .paired } }

    var body: some View {
        VStack(spacing: 16) {
            if let device {
                if let media = device.media {
                    NowPlayingCard(deviceId: device.id, media: media, model: model)
                }

                TvTouchpad(press: press)
                    .frame(height: 190)

                HStack(alignment: .center, spacing: 22) {
                    DPad(press: press)
                    sideKeys
                }

                mediaKeys
            } else {
                gone
            }
        }
        .padding(20)
        .frame(width: 360)
        .background(Theme.panel)
    }

    private var sideKeys: some View {
        VStack(spacing: 10) {
            HStack(spacing: 10) {
                RemoteKey(icon: .arrowLeft, help: "Back") { press(.key(TvRemote.back)) }
                RemoteKey(icon: .house, help: "Home") { press(.key(TvRemote.home)) }
            }
            HStack(spacing: 10) {
                RemoteKey(icon: .ellipsis, help: "Menu") { press(.key(TvRemote.menu)) }
                RemoteKey(icon: .volumeX, help: "Mute") { press(.key(TvRemote.mute)) }
            }
            HStack(spacing: 10) {
                RemoteKey(icon: .volume1, help: "Volume down") { press(.key(TvRemote.volumeDown)) }
                RemoteKey(icon: .volume2, help: "Volume up") { press(.key(TvRemote.volumeUp)) }
            }
        }
    }

    private var mediaKeys: some View {
        HStack(spacing: 10) {
            RemoteKey(icon: .skipBack, help: "Previous") { press(.key(TvRemote.previous)) }
            RemoteKey(icon: .play, help: "Play or pause", wide: true) { press(.key(TvRemote.playPause)) }
            RemoteKey(icon: .skipForward, help: "Next") { press(.key(TvRemote.next)) }
        }
    }

    private var gone: some View {
        VStack(spacing: 10) {
            Image(lucide: .unplug, size: 36, weight: .light)
                .foregroundStyle(Theme.accent)
            Text("Your TV isn't connected").font(.system(size: 15, weight: .semibold))
        }
        .frame(maxWidth: .infinity, minHeight: 240)
    }

    private func press(_ press: TvRemote) {
        model.press(press, on: deviceId)
    }
}

/// Up, down, left, right and OK in a ring, like a TV remote.
private struct DPad: View {
    let press: (TvRemote) -> Void

    var body: some View {
        ZStack {
            Circle().fill(Theme.card)
            Circle().strokeBorder(Theme.border)

            VStack {
                arrow(.chevronUp, TvRemote.up)
                Spacer()
                arrow(.chevronDown, TvRemote.down)
            }
            .padding(6)
            HStack {
                arrow(.chevronLeft, TvRemote.left)
                Spacer()
                arrow(.chevronRight, TvRemote.right)
            }
            .padding(6)

            Button { press(.key(TvRemote.select)) } label: {
                Text("OK")
                    .font(.system(size: 14, weight: .bold))
                    .foregroundStyle(Theme.onAccent)
                    .frame(width: 58, height: 58)
                    .background(Circle().fill(Theme.accent))
                    .contentShape(Circle())
            }
            .buttonStyle(PressStyle())
        }
        .frame(width: 164, height: 164)
    }

    private func arrow(_ icon: Lucide, _ key: String) -> some View {
        RemoteKey(icon: icon, help: "", round: true) { press(.key(key)) }
    }
}

/// One round or rounded-square key.
private struct RemoteKey: View {
    let icon: Lucide
    let help: String
    var round = false
    var wide = false
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(lucide: icon, size: 15, weight: .semibold)
                .frame(width: wide ? 76 : 40, height: 40)
                .background(shape.fill(hovering ? Theme.tint : (round ? Color.clear : Theme.card)))
                .overlay(shape.stroke(round ? Color.clear : Theme.border))
                .contentShape(shape)
        }
        .buttonStyle(PressStyle())
        .foregroundStyle(Color.primary)
        .help(help)
        .onHover { hovering = $0 }
    }

    private var shape: RoundedRectangle {
        RoundedRectangle(cornerRadius: round ? 20 : 12, style: .continuous)
    }
}

/// The touchpad, with a hint on what to do with it.
private struct TvTouchpad: View {
    let press: (TvRemote) -> Void

    @State private var holding = false

    var body: some View {
        ZStack {
            TouchpadSurface(press: press, holding: $holding)

            VStack(spacing: 8) {
                Image(lucide: .mousePointer2, size: 22, weight: .light)
                    .foregroundStyle(Theme.accent)
                Text(holding ? "Press ⌘ to let go" : "Click to use the pointer")
                    .font(.system(size: 12, weight: .medium))
                Text("Your typing goes to the TV")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
            .allowsHitTesting(false)
        }
        .background(RoundedRectangle(cornerRadius: Theme.radius, style: .continuous).fill(Theme.card))
        .overlay(
            RoundedRectangle(cornerRadius: Theme.radius, style: .continuous)
                .strokeBorder(holding ? Theme.accent : Theme.border, lineWidth: holding ? 2 : 1)
        )
        .clipShape(RoundedRectangle(cornerRadius: Theme.radius, style: .continuous))
    }
}

private struct TouchpadSurface: NSViewRepresentable {
    let press: (TvRemote) -> Void
    @Binding var holding: Bool

    func makeNSView(context: Context) -> TvTouchpadView {
        let view = TvTouchpadView()
        view.press = press
        view.onHold = { holding = $0 }
        return view
    }

    func updateNSView(_ view: TvTouchpadView, context: Context) {
        view.press = press
    }
}

/// Holds this Mac's pointer once clicked, so the trackpad or mouse moves the TV's pointer instead,
/// and turns the keyboard's keys into the TV remote's.
final class TvTouchpadView: NSView {
    var press: (TvRemote) -> Void = { _ in }
    var onHold: (Bool) -> Void = { _ in }

    private(set) var holding = false
    private var tracking: NSTrackingArea?
    private var move = CGVector.zero
    private var scroll = CGVector.zero
    private var movePending = false
    private var scrollPending = false

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

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
        letGo()
        return super.resignFirstResponder()
    }

    /// Gives this Mac its pointer back.
    func letGo() {
        guard holding else { return }

        holding = false
        CGAssociateMouseAndMouseCursorPosition(1)
        NSCursor.unhide()
        onHold(false)
    }

    private func hold() {
        guard !holding else { return }

        holding = true
        CGAssociateMouseAndMouseCursorPosition(0)
        NSCursor.hide()
        onHold(true)
    }

    // MARK: Pointer

    override func mouseMoved(with event: NSEvent) { moved(event) }
    override func mouseDragged(with event: NSEvent) { moved(event) }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)

        if holding { press(.click) } else { hold() }
    }

    override func rightMouseDown(with event: NSEvent) {
        if holding { press(.key(TvRemote.back)) }
    }

    override func scrollWheel(with event: NSEvent) {
        // A mouse wheel reports lines; a trackpad reports points. One line is about one notch.
        let scale: CGFloat = event.hasPreciseScrollingDeltas ? 1 : 30
        scroll.dx += event.scrollingDeltaX * scale
        scroll.dy += event.scrollingDeltaY * scale

        // Each scroll is a swipe on the TV, so they go out about 10 times a second, like the phone's touchpad.
        guard !scrollPending else { return }
        scrollPending = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { [weak self] in
            guard let self else { return }
            scrollPending = false
            send(scroll, as: TvRemote.scroll)
            scroll = .zero
        }
    }

    private func moved(_ event: NSEvent) {
        guard holding else { return }

        move.dx += event.deltaX
        move.dy += event.deltaY

        guard !movePending else { return }
        movePending = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.016) { [weak self] in
            guard let self else { return }
            movePending = false
            send(move, as: TvRemote.move)
            move = .zero
        }
    }

    private func send(_ step: CGVector, as make: (Double, Double) -> TvRemote) {
        guard step != .zero else { return }

        let limit = TvRemote.maxStep
        press(make(min(max(Double(step.dx), -limit), limit), min(max(Double(step.dy), -limit), limit)))
    }

    // MARK: Keys

    override func flagsChanged(with event: NSEvent) {
        if event.modifierFlags.contains(.command) { letGo() }
    }

    override func keyDown(with event: NSEvent) {
        if let key = Self.key(for: event.keyCode) {
            press(.key(key))
        } else if !event.modifierFlags.contains(.command), !event.modifierFlags.contains(.control),
                  let text = event.characters, !text.isEmpty, text.unicodeScalars.allSatisfy(Self.typeable) {
            press(.text(text))
        } else {
            super.keyDown(with: event)
        }
    }

    /// Leaves out control characters and the function keys, which macOS reports as private-use characters.
    private static func typeable(_ scalar: Unicode.Scalar) -> Bool {
        !CharacterSet.controlCharacters.contains(scalar) && !(0xF700...0xF8FF).contains(scalar.value)
    }

    /// The Mac keys that act like the TV remote's.
    static func key(for code: UInt16) -> String? {
        switch code {
        case 126: return TvRemote.up
        case 125: return TvRemote.down
        case 123: return TvRemote.left
        case 124: return TvRemote.right
        case 36, 76: return TvRemote.select
        case 53: return TvRemote.back
        case 51: return TvRemote.delete
        default: return nil
        }
    }
}

/// The window with a TV's remote. It shows one TV at a time.
@MainActor
final class TvRemoteWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private weak var model: AppModel?

    init(model: AppModel) {
        self.model = model
    }

    func show(deviceId: String, name: String) {
        guard let model else { return }
        NSApplication.shared.activate(ignoringOtherApps: true)

        let window = window ?? makeWindow()
        window.title = "\(name) remote"
        // The window grows and shrinks with what's playing.
        let hosting = NSHostingView(rootView: TvRemoteWindow(model: model, deviceId: deviceId))
        hosting.sizingOptions = [.intrinsicContentSize]
        window.contentView = hosting
        window.makeKeyAndOrderFront(nil)
        self.window = window
    }

    private func makeWindow() -> NSWindow {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 360, height: 560),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.center()
        window.setFrameAutosaveName("TvRemote")
        return window
    }

    func windowDidResignKey(_ notification: Notification) {
        (window?.firstResponder as? TvTouchpadView)?.letGo()
    }

    func windowWillClose(_ notification: Notification) {
        (window?.firstResponder as? TvTouchpadView)?.letGo()
    }
}
