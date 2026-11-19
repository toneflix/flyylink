import AppKit
import AVFoundation
import SwiftUI
import FlyyLinkKit

/// The phone's live screen, with its Back, Home and Recents buttons underneath.
struct ScreenWindow: View {
    @ObservedObject var mirror: ScreenMirror

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                ScreenSurface(decoder: mirror.decoder, videoSize: mirror.videoSize) { mirror.input($0) }

                if mirror.phase != .live {
                    status
                }
            }

            controls
        }
        .background(Color.black)
        .preferredColorScheme(.dark)
    }

    private var status: some View {
        VStack(spacing: 12) {
            Image(lucide: mirror.phase == .ended ? .unplug : .cast, size: 40, weight: .light)
                .foregroundStyle(Theme.accent)

            Text(statusTitle).font(.system(size: 15, weight: .semibold))
            Text(statusHint)
                .font(.system(size: 12))
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 260)

            if mirror.phase == .waiting { ProgressView().controlSize(.small) }
            if mirror.phase == .ended, let id = mirror.deviceId {
                Button("Try again") { mirror.begin(deviceId: id, name: mirror.deviceName) }
                    .buttonStyle(.borderedProminent)
            }
        }
        .padding(24)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.black)
    }

    private var statusTitle: String {
        switch mirror.phase {
        case .ended: return "Mirroring stopped"
        case .idle: return "Not mirroring"
        default: return "Allow on \(mirror.deviceName)"
        }
    }

    private var statusHint: String {
        switch mirror.phase {
        case .ended: return "Your phone stopped sharing its screen or disconnected."
        case .idle: return "Start it from Mirror in the FlyyLink menu."
        default: return "Tap the notification on your phone, then \"Start now\"."
        }
    }

    private var controls: some View {
        HStack(spacing: 34) {
            control(.chevronLeft, help: "Back (Esc or right-click)") { mirror.input(.key(ScreenInput.back)) }
            control(.circle, help: "Home") { mirror.input(.key(ScreenInput.home)) }
            control(.copy, help: "Recent apps") { mirror.input(.key(ScreenInput.recents)) }
            control(.bell, help: "Notifications") { mirror.input(.key(ScreenInput.notifications)) }
            control(mirror.isMuted ? .volumeX : .volume2, help: mirror.isMuted ? "Play the phone's sound" : "Mute the phone's sound") {
                mirror.isMuted.toggle()
            }
        }
        .frame(maxWidth: .infinity)
        .frame(height: 40)
        .background(Color(white: 0.08))
        .disabled(mirror.phase != .live)
    }

    private func control(_ icon: Lucide, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(lucide: icon, size: 14, weight: .semibold)
                .frame(width: 34, height: 28)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(.white.opacity(0.85))
        .help(help)
    }
}

/// Hosts the decoder's layer and turns mouse and keyboard into taps, swipes and typing.
private struct ScreenSurface: NSViewRepresentable {
    let decoder: ScreenDecoder
    let videoSize: CGSize
    let onInput: (ScreenInput) -> Void

    func makeNSView(context: Context) -> ScreenSurfaceView {
        ScreenSurfaceView(display: decoder.layer)
    }

    func updateNSView(_ view: ScreenSurfaceView, context: Context) {
        view.videoSize = videoSize
        view.onInput = onInput
    }
}

final class ScreenSurfaceView: NSView {
    var videoSize = CGSize.zero
    var onInput: ((ScreenInput) -> Void)?

    private let display: AVSampleBufferDisplayLayer
    private var pressed: (point: CGPoint, time: Date)?
    private var scrolled = CGSize.zero
    private var scrollEnd: Timer?

    /// Further than this, in points, a click becomes a swipe.
    private static let dragDistance: CGFloat = 6
    private static let longPress: TimeInterval = 0.5

    init(display: AVSampleBufferDisplayLayer) {
        self.display = display
        super.init(frame: .zero)

        wantsLayer = true
        layer = CALayer()
        layer?.backgroundColor = NSColor.black.cgColor
        layer?.addSublayer(display)
    }

    required init?(coder: NSCoder) { nil }

    override var acceptsFirstResponder: Bool { true }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        window?.makeFirstResponder(self)
    }

    override func layout() {
        super.layout()

        CATransaction.begin()
        CATransaction.setDisableActions(true)
        display.frame = bounds
        CATransaction.commit()
    }

    /// Where the picture sits inside the view, since it keeps its shape.
    private var pictureRect: CGRect {
        guard videoSize.width > 0, videoSize.height > 0 else { return bounds }
        return AVMakeRect(aspectRatio: videoSize, insideRect: bounds)
    }

    /// A point in the view as fractions of the picture, from its top left, kept inside it.
    private func fraction(_ point: CGPoint) -> CGPoint {
        let rect = pictureRect
        let x = (point.x - rect.minX) / max(rect.width, 1)
        let y = 1 - (point.y - rect.minY) / max(rect.height, 1)
        return CGPoint(x: min(max(x, 0), 1), y: min(max(y, 0), 1))
    }

    private func location(_ event: NSEvent) -> CGPoint {
        convert(event.locationInWindow, from: nil)
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)

        let point = location(event)
        guard pictureRect.contains(point) else { return }
        pressed = (point, Date())
    }

    override func mouseUp(with event: NSEvent) {
        guard let start = pressed else { return }
        pressed = nil

        let end = location(event)
        let held = Date().timeIntervalSince(start.time)
        let from = fraction(start.point)

        if hypot(end.x - start.point.x, end.y - start.point.y) < Self.dragDistance {
            onInput?(held >= Self.longPress ? .longPress(x: from.x, y: from.y) : .tap(x: from.x, y: from.y))
        } else {
            let to = fraction(end)
            let duration = Int64(min(max(held, 0.08), 2) * 1000)
            onInput?(.swipe(x: from.x, y: from.y, toX: to.x, toY: to.y, durationMs: duration))
        }
    }

    /// Right-click goes back, like a mouse on Android.
    override func rightMouseDown(with event: NSEvent) {
        onInput?(.key(ScreenInput.back))
    }

    /// Scrolling becomes one swipe from the middle, sent once the wheel or trackpad settles.
    override func scrollWheel(with event: NSEvent) {
        let scale: CGFloat = event.hasPreciseScrollingDeltas ? 1 : 12
        scrolled.width += event.scrollingDeltaX * scale
        scrolled.height += event.scrollingDeltaY * scale

        scrollEnd?.invalidate()
        scrollEnd = Timer.scheduledTimer(withTimeInterval: 0.08, repeats: false) { [weak self] _ in
            Task { @MainActor in self?.sendScroll() }
        }
    }

    private func sendScroll() {
        let rect = pictureRect
        let dx = scrolled.width / max(rect.width, 1)
        let dy = scrolled.height / max(rect.height, 1)
        scrolled = .zero
        guard abs(dx) > 0.01 || abs(dy) > 0.01 else { return }

        func clamp(_ value: CGFloat) -> Double { Double(min(max(value, 0.05), 0.95)) }
        onInput?(.swipe(x: 0.5, y: 0.5, toX: clamp(0.5 + dx), toY: clamp(0.5 + dy), durationMs: 250))
    }

    // MARK: Keyboard

    /// Keys go through the Mac's own key bindings, so typing, dead keys, arrows and Option or Command
    /// movement behave as they do in any Mac text field, and reach the phone as text or edit keys.
    override func keyDown(with event: NSEvent) {
        interpretKeyEvents([event])
    }

    override func insertText(_ insertString: Any) {
        let text = (insertString as? NSAttributedString)?.string ?? (insertString as? String) ?? ""
        guard !text.isEmpty else { return }

        onInput?(.text(text))
    }

    override func doCommand(by selector: Selector) {
        guard let key = Self.commands[NSStringFromSelector(selector)] else {
            super.doCommand(by: selector)
            return
        }

        onInput?(.key(key.name, select: key.select))
    }

    /// Command shortcuts that edit the phone's text instead of this app's menus.
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard window?.isKeyWindow == true,
              event.modifierFlags.intersection(.deviceIndependentFlagsMask) == .command,
              let character = event.charactersIgnoringModifiers?.lowercased()
        else { return super.performKeyEquivalent(with: event) }

        switch character {
        case "a": onInput?(.key(ScreenInput.selectAll))
        case "c": onInput?(.key(ScreenInput.copy))
        case "x": onInput?(.key(ScreenInput.cut))
        case "v": paste()
        default: return super.performKeyEquivalent(with: event)
        }

        return true
    }

    /// Types what is on this Mac's clipboard into the phone.
    private func paste() {
        guard let text = NSPasteboard.general.string(forType: .string), !text.isEmpty else { return }

        onInput?(.text(text))
    }

    /// Key binding selectors and the phone key each one sends.
    private static let commands: [String: (name: String, select: Bool)] = [
        "deleteBackward:": (ScreenInput.delete, false),
        "deleteForward:": (ScreenInput.deleteForward, false),
        "deleteWordBackward:": (ScreenInput.deleteWord, false),
        "insertNewline:": (ScreenInput.enter, false),
        "insertLineBreak:": (ScreenInput.enter, false),
        "insertTab:": (ScreenInput.tab, false),
        "insertBacktab:": (ScreenInput.tab, true),
        "cancelOperation:": (ScreenInput.back, false),
        "moveLeft:": (ScreenInput.left, false),
        "moveRight:": (ScreenInput.right, false),
        "moveUp:": (ScreenInput.up, false),
        "moveDown:": (ScreenInput.down, false),
        "moveWordLeft:": (ScreenInput.wordLeft, false),
        "moveWordRight:": (ScreenInput.wordRight, false),
        "moveToBeginningOfLine:": (ScreenInput.lineStart, false),
        "moveToLeftEndOfLine:": (ScreenInput.lineStart, false),
        "moveToEndOfLine:": (ScreenInput.lineEnd, false),
        "moveToRightEndOfLine:": (ScreenInput.lineEnd, false),
        "moveLeftAndModifySelection:": (ScreenInput.left, true),
        "moveRightAndModifySelection:": (ScreenInput.right, true),
        "moveUpAndModifySelection:": (ScreenInput.up, true),
        "moveDownAndModifySelection:": (ScreenInput.down, true),
        "moveWordLeftAndModifySelection:": (ScreenInput.wordLeft, true),
        "moveWordRightAndModifySelection:": (ScreenInput.wordRight, true),
        "moveToBeginningOfLineAndModifySelection:": (ScreenInput.lineStart, true),
        "moveToLeftEndOfLineAndModifySelection:": (ScreenInput.lineStart, true),
        "moveToEndOfLineAndModifySelection:": (ScreenInput.lineEnd, true),
        "moveToRightEndOfLineAndModifySelection:": (ScreenInput.lineEnd, true),
        "selectAll:": (ScreenInput.selectAll, false),
    ]
}
