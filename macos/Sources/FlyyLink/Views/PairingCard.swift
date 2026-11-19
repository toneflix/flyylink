import SwiftUI
import FlyyLinkKit

/// The pairing prompt as a card in the menu bar panel.
struct PairingCard: View {
    let device: DeviceState
    @ObservedObject var model: AppModel

    var body: some View {
        PairingPrompt(device: device, model: model)
            .frame(maxWidth: .infinity)
            .card(padding: 20)
    }
}

/// Asks to confirm a pairing by comparing the code shown on both screens.
struct PairingPrompt: View {
    let device: DeviceState
    @ObservedObject var model: AppModel

    private var isOurRequest: Bool { device.pairState == .requested }

    private var hint: String {
        let other = device.isComputer ? "the other computer" : device.isTv ? "the TV" : "your phone"

        return isOurRequest ? "Accept on \(other) if the code matches" : "Same code on \(other)?"
    }

    var body: some View {
        VStack(spacing: 18) {
            HStack(spacing: 14) {
                DeviceArt(type: device.type, width: 36)
                SignalDots()
                LaptopArt(width: 76)
            }
            .padding(.top, 6)

            VStack(spacing: 6) {
                Text(isOurRequest ? "Waiting for \(device.name)" : "Pair with \(device.name)?")
                    .font(.system(size: 18, weight: .bold))
                    .multilineTextAlignment(.center)
                Text(hint)
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
            }

            if let code = device.code {
                CodeDigits(code: code)
            }

            if isOurRequest {
                ProgressView().controlSize(.small)
            } else {
                HStack(spacing: 8) {
                    Button("Not now") { model.reject(device.id) }
                        .buttonStyle(PillStyle(prominent: false))
                    Button("Pair") { model.accept(device.id) }
                        .buttonStyle(PillStyle(prominent: true))
                        .keyboardShortcut(.defaultAction)
                }
            }
        }
    }
}

/// The verification code as big separate characters, split in two halves.
struct CodeDigits: View {
    let code: String

    var body: some View {
        let characters = Array(code.filter { !$0.isWhitespace })
        let half = (characters.count + 1) / 2

        HStack(spacing: 5) {
            ForEach(Array(characters.enumerated()), id: \.offset) { index, character in
                if index == half { Spacer().frame(width: 6) }
                Text(String(character))
                    .font(.system(size: 20, weight: .semibold, design: .monospaced))
                    .foregroundStyle(Theme.accentStrong)
                    .frame(width: 28, height: 40)
                    .background(Theme.tint, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Verification code \(code)")
    }
}

/// Three dots fading from the phone toward the Mac, pulsing one after another.
private struct SignalDots: View {
    @State private var phase = 0
    private let timer = Timer.publish(every: 0.4, on: .main, in: .common).autoconnect()

    var body: some View {
        HStack(spacing: 5) {
            ForEach(0..<3) { index in
                Circle()
                    .fill(Theme.accent)
                    .frame(width: 6, height: 6)
                    .opacity(index == phase ? 1 : 0.3)
            }
        }
        .animation(.easeInOut(duration: 0.3), value: phase)
        .onReceive(timer) { _ in phase = (phase + 1) % 3 }
    }
}

/// A full-width rounded button, filled when prominent.
struct PillStyle: ButtonStyle {
    let prominent: Bool

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(prominent ? Theme.onAccent : Color.primary)
            .frame(maxWidth: .infinity)
            .frame(height: 38)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(prominent ? Theme.accent : Theme.card)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .stroke(prominent ? Color.clear : Color.primary.opacity(0.14))
            )
            .opacity(configuration.isPressed ? 0.8 : 1)
            .contentShape(Rectangle())
    }
}
