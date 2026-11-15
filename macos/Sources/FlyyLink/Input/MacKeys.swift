import Carbon.HIToolbox
import CoreGraphics

/// Maps the W3C key names that travel between computers to and from this Mac's key codes.
enum MacKeys {
    static let codes: [String: Int] = {
        let map: [String: Int] = [
            "KeyA": kVK_ANSI_A, "KeyB": kVK_ANSI_B, "KeyC": kVK_ANSI_C, "KeyD": kVK_ANSI_D, "KeyE": kVK_ANSI_E,
            "KeyF": kVK_ANSI_F, "KeyG": kVK_ANSI_G, "KeyH": kVK_ANSI_H, "KeyI": kVK_ANSI_I, "KeyJ": kVK_ANSI_J,
            "KeyK": kVK_ANSI_K, "KeyL": kVK_ANSI_L, "KeyM": kVK_ANSI_M, "KeyN": kVK_ANSI_N, "KeyO": kVK_ANSI_O,
            "KeyP": kVK_ANSI_P, "KeyQ": kVK_ANSI_Q, "KeyR": kVK_ANSI_R, "KeyS": kVK_ANSI_S, "KeyT": kVK_ANSI_T,
            "KeyU": kVK_ANSI_U, "KeyV": kVK_ANSI_V, "KeyW": kVK_ANSI_W, "KeyX": kVK_ANSI_X, "KeyY": kVK_ANSI_Y,
            "KeyZ": kVK_ANSI_Z,
            "Digit0": kVK_ANSI_0, "Digit1": kVK_ANSI_1, "Digit2": kVK_ANSI_2, "Digit3": kVK_ANSI_3, "Digit4": kVK_ANSI_4,
            "Digit5": kVK_ANSI_5, "Digit6": kVK_ANSI_6, "Digit7": kVK_ANSI_7, "Digit8": kVK_ANSI_8, "Digit9": kVK_ANSI_9,
            "F1": kVK_F1, "F2": kVK_F2, "F3": kVK_F3, "F4": kVK_F4, "F5": kVK_F5, "F6": kVK_F6,
            "F7": kVK_F7, "F8": kVK_F8, "F9": kVK_F9, "F10": kVK_F10, "F11": kVK_F11, "F12": kVK_F12,
            "Numpad0": kVK_ANSI_Keypad0, "Numpad1": kVK_ANSI_Keypad1, "Numpad2": kVK_ANSI_Keypad2, "Numpad3": kVK_ANSI_Keypad3,
            "Numpad4": kVK_ANSI_Keypad4, "Numpad5": kVK_ANSI_Keypad5, "Numpad6": kVK_ANSI_Keypad6, "Numpad7": kVK_ANSI_Keypad7,
            "Numpad8": kVK_ANSI_Keypad8, "Numpad9": kVK_ANSI_Keypad9, "NumpadDecimal": kVK_ANSI_KeypadDecimal,
            "NumpadAdd": kVK_ANSI_KeypadPlus, "NumpadSubtract": kVK_ANSI_KeypadMinus, "NumpadMultiply": kVK_ANSI_KeypadMultiply,
            "NumpadDivide": kVK_ANSI_KeypadDivide, "NumpadEnter": kVK_ANSI_KeypadEnter,
            "Enter": kVK_Return, "Escape": kVK_Escape, "Backspace": kVK_Delete, "Tab": kVK_Tab, "Space": kVK_Space,
            "Minus": kVK_ANSI_Minus, "Equal": kVK_ANSI_Equal, "BracketLeft": kVK_ANSI_LeftBracket, "BracketRight": kVK_ANSI_RightBracket,
            "Backslash": kVK_ANSI_Backslash, "Semicolon": kVK_ANSI_Semicolon, "Quote": kVK_ANSI_Quote, "Backquote": kVK_ANSI_Grave,
            "Comma": kVK_ANSI_Comma, "Period": kVK_ANSI_Period, "Slash": kVK_ANSI_Slash, "IntlBackslash": kVK_ISO_Section,
            "CapsLock": kVK_CapsLock,
            "ArrowLeft": kVK_LeftArrow, "ArrowRight": kVK_RightArrow, "ArrowUp": kVK_UpArrow, "ArrowDown": kVK_DownArrow,
            "Home": kVK_Home, "End": kVK_End, "PageUp": kVK_PageUp, "PageDown": kVK_PageDown,
            "Delete": kVK_ForwardDelete, "Insert": kVK_Help,
            "ShiftLeft": kVK_Shift, "ShiftRight": kVK_RightShift, "ControlLeft": kVK_Control, "ControlRight": kVK_RightControl,
            "AltLeft": kVK_Option, "AltRight": kVK_RightOption, "MetaLeft": kVK_Command, "MetaRight": kVK_RightCommand,
        ]
        return map
    }()

    static let names: [Int: String] = Dictionary(codes.map { ($0.value, $0.key) }, uniquingKeysWith: { first, _ in first })

    /// The NX_KEYTYPE of a volume or media key.
    static func mediaKey(_ name: String) -> Int? {
        switch name {
        case "AudioVolumeUp": return 0
        case "AudioVolumeDown": return 1
        case "AudioVolumeMute": return 7
        case "MediaPlayPause": return 16
        case "MediaTrackNext": return 17
        case "MediaTrackPrevious": return 18
        default: return nil
        }
    }

    static func keyCode(_ name: String) -> CGKeyCode? { codes[name].map { CGKeyCode($0) } }

    static func name(_ keyCode: Int64) -> String? { names[Int(keyCode)] }

    /// The modifier flag a modifier key holds down, used to tell its press from its release.
    static func flag(for name: String) -> CGEventFlags? {
        switch name {
        case "ShiftLeft", "ShiftRight": return .maskShift
        case "ControlLeft", "ControlRight": return .maskControl
        case "AltLeft", "AltRight": return .maskAlternate
        case "MetaLeft", "MetaRight": return .maskCommand
        case "CapsLock": return .maskAlphaShift
        default: return nil
        }
    }
}
