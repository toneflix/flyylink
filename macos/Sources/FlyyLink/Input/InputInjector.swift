import AppKit
import FlyyLinkKit

/// Plays keyboard and mouse input from another computer or a phone on this Mac. Runs on the main thread. Needs Accessibility access.
final class InputInjector {
    /// Sends to a device; false when it isn't connected.
    var send: (String, RemoteInput) -> Bool = { _, _ in false }

    /// The computer now working this Mac, or nil.
    var onChange: ((String?) -> Void)?

    private(set) var controllerId: String?

    /// The edge the pointer came in by, which it leaves by again. Nil when it came from the screen viewer.
    private var entry: ScreenEdge?
    private var swapsControlAndMeta = false
    private var cursor = CGPoint.zero
    private var buttons: Set<RemoteInput.MouseButton> = []
    private var keys: Set<String> = []
    private var flags: CGEventFlags = []
    private var lastClick: (button: RemoteInput.MouseButton, at: Date, point: CGPoint, count: Int64)?
    private let source = CGEventSource(stateID: .hidSystemState)

    func handle(_ input: RemoteInput, from deviceId: String) {
        switch input {
        case .enter(let edge, let position, let platform):
            begin(deviceId, entry: edge, platform: platform)
            moveCursor(to: DesktopBounds.current().point(on: edge, at: position, inset: 6))
        case .begin(let platform, _):
            begin(deviceId, entry: nil, platform: platform)
        case .moveTo(let x, let y):
            if controllerId != deviceId { begin(deviceId, entry: nil, platform: "") }
            let main = DesktopBounds.main()
            moveCursor(to: CGPoint(x: main.minX + x * (main.maxX - main.minX), y: main.minY + y * (main.maxY - main.minY)))
        case .release:
            guard controllerId == deviceId else { return }
            end()
        case .leave:
            return
        case .move(let dx, let dy):
            guard controllerId == deviceId else { return }
            move(dx: dx, dy: dy)
        case .button(let button, let down):
            guard controllerId == deviceId else { return }
            press(button, down: down)
        case .scroll(let dx, let dy):
            guard controllerId == deviceId else { return }
            CGEvent(scrollWheelEvent2Source: source, units: .pixel, wheelCount: 2, wheel1: Int32(dy.rounded()), wheel2: Int32(-dx.rounded()), wheel3: 0)?
                .post(tap: .cghidEventTap)
        case .key(let code, let down):
            guard controllerId == deviceId else { return }
            key(swapsControlAndMeta ? KeyCodes.swappingControlAndMeta(code) : code, down: down)
        case .text(let text):
            guard controllerId == deviceId else { return }
            type(text)
        }
    }

    /// The controlling computer went away: let go of everything it held.
    func lost(_ deviceId: String) {
        if controllerId == deviceId { end() }
    }

    private func begin(_ deviceId: String, entry: ScreenEdge?, platform: String) {
        if controllerId != nil, controllerId != deviceId { end() }

        controllerId = deviceId
        self.entry = entry
        swapsControlAndMeta = platform == RemoteInput.windows
        cursor = CGEvent(source: nil)?.location ?? .zero
        onChange?(deviceId)
    }

    private func end() {
        buttons.forEach { press($0, down: false) }
        keys.forEach { key($0, down: false) }
        buttons = []
        keys = []
        flags = []
        controllerId = nil
        entry = nil
        onChange?(nil)
    }

    private func move(dx: Double, dy: Double) {
        let bounds = DesktopBounds.current()
        let target = CGPoint(x: cursor.x + dx, y: cursor.y + dy)

        if let entry, let controllerId, bounds.reached(entry, by: target), Self.heads(toward: entry, dx: dx, dy: dy) {
            let position = bounds.position(along: entry, of: target)
            _ = send(controllerId, .leave(edge: entry, position: position))
            end()
            return
        }

        moveCursor(to: bounds.clamp(target))
    }

    private static func heads(toward edge: ScreenEdge, dx: Double, dy: Double) -> Bool {
        switch edge {
        case .left: return dx < 0
        case .right: return dx > 0
        case .top: return dy < 0
        case .bottom: return dy > 0
        }
    }

    private func moveCursor(to point: CGPoint) {
        cursor = point

        let type: CGEventType
        if buttons.contains(.left) {
            type = .leftMouseDragged
        } else if buttons.contains(.right) {
            type = .rightMouseDragged
        } else if buttons.contains(.middle) {
            type = .otherMouseDragged
        } else {
            type = .mouseMoved
        }

        let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: point, mouseButton: .left)
        event?.flags = flags
        event?.post(tap: .cghidEventTap)
    }

    private func press(_ button: RemoteInput.MouseButton, down: Bool) {
        let type: CGEventType
        let cgButton: CGMouseButton
        switch button {
        case .left:
            type = down ? .leftMouseDown : .leftMouseUp
            cgButton = .left
        case .right:
            type = down ? .rightMouseDown : .rightMouseUp
            cgButton = .right
        case .middle:
            type = down ? .otherMouseDown : .otherMouseUp
            cgButton = .center
        }

        if down {
            buttons.insert(button)
        } else {
            buttons.remove(button)
        }

        guard let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: cursor, mouseButton: cgButton) else { return }

        event.setIntegerValueField(.mouseEventClickState, value: clickCount(button, down: down))
        event.flags = flags
        event.post(tap: .cghidEventTap)
    }

    /// Counts quick presses in the same place, so double and triple clicks work.
    private func clickCount(_ button: RemoteInput.MouseButton, down: Bool) -> Int64 {
        guard down else { return lastClick?.count ?? 1 }

        let now = Date()
        var count: Int64 = 1
        if let last = lastClick, last.button == button, now.timeIntervalSince(last.at) < NSEvent.doubleClickInterval,
           abs(last.point.x - cursor.x) < 5, abs(last.point.y - cursor.y) < 5 {
            count = last.count + 1
        }
        lastClick = (button, now, cursor, count)

        return count
    }

    private func key(_ code: String, down: Bool) {
        if let media = MacKeys.mediaKey(code) {
            postMedia(media, down: down)
            return
        }
        guard let keyCode = MacKeys.keyCode(code) else { return }

        if let flag = MacKeys.flag(for: code), code != "CapsLock" {
            if down { flags.insert(flag) } else { flags.remove(flag) }
        }
        if down { keys.insert(code) } else { keys.remove(code) }

        let event = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: down)
        event?.flags = flags
        event?.post(tap: .cghidEventTap)
    }

    /// Volume and media keys are system events on a Mac, not key codes.
    private func postMedia(_ media: Int, down: Bool) {
        let state = down ? 0xA : 0xB
        let event = NSEvent.otherEvent(
            with: .systemDefined,
            location: .zero,
            modifierFlags: NSEvent.ModifierFlags(rawValue: UInt(state << 8)),
            timestamp: 0,
            windowNumber: 0,
            context: nil,
            subtype: 8,
            data1: (media << 16) | (state << 8),
            data2: -1
        )
        event?.cgEvent?.post(tap: .cghidEventTap)
    }

    /// Types each character as itself, so text arrives right whatever the keyboard layout. New lines press Return.
    private func type(_ text: String) {
        for character in text {
            if character.isNewline {
                key("Enter", down: true)
                key("Enter", down: false)
                continue
            }

            let units = Array(String(character).utf16)
            for down in [true, false] {
                let event = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: down)
                event?.flags = []
                event?.keyboardSetUnicodeString(stringLength: units.count, unicodeString: units)
                event?.post(tap: .cghidEventTap)
            }
        }
    }
}
