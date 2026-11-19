import AppKit
import SwiftUI

/// The guided setup for hearing calls on this Mac: Bluetooth on, pair once, then connect.
struct CallAudioWindow: View {
    @ObservedObject var audio: HandsFreeAudio

    var body: some View {
        VStack(spacing: 0) {
            CallAudioHero(isLive: audio.isReady)
                .padding(.top, 28)

            VStack(spacing: 6) {
                HStack(spacing: 8) {
                    Text("Hear calls on your Mac").font(.system(size: 20, weight: .bold))
                    ExperimentalBadge()
                }
                Text("It works like a Bluetooth headset, and you only need to pair once.")
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: 360)
            }
            .padding(.top, 18)

            VStack(spacing: 10) {
                SetupStep(number: 1, title: "Turn on Bluetooth", state: bluetoothStep) { bluetoothDetail }
                SetupStep(number: 2, title: "Pair your phone with this Mac", state: pairStep) { pairDetail }
                SetupStep(number: 3, title: "Connect for calls", state: connectStep) { connectDetail }
            }
            .padding(.top, 22)

            Spacer(minLength: 16)

            footer
        }
        .padding(.horizontal, 24)
        .padding(.bottom, 20)
        .frame(width: 480, height: 600)
        .glassBackdrop()
        .animation(.spring(response: 0.35, dampingFraction: 0.85), value: audio.stage)
        .onAppear { audio.refresh() }
    }

    // MARK: Steps

    private var bluetoothStep: StepState {
        switch audio.stage {
        case .noPhone, .checking: return .pending
        case .macOff, .phoneOff, .notAllowed: return .active
        default: return .done
        }
    }

    private var pairStep: StepState {
        switch audio.stage {
        case .notPaired, .pairing: return .active
        case .failed: return audio.isPaired ? .done : .active
        case .paused, .connecting, .ready: return .done
        default: return .pending
        }
    }

    private var connectStep: StepState {
        switch audio.stage {
        case .ready: return .done
        case .connecting, .paused: return .active
        case .failed: return audio.isPaired ? .active : .pending
        default: return .pending
        }
    }

    private var bluetoothDetail: AnyView {
        switch audio.stage {
        case .macOff:
            return AnyView(StepHint(icon: .laptop, text: "Turn on Bluetooth on this Mac", action: ("Open Bluetooth Settings", audio.openBluetoothSettings)))
        case .phoneOff:
            return AnyView(StepHint(icon: .smartphone, text: "Turn on Bluetooth on your phone", action: ("Check again", audio.refresh)))
        case .notAllowed:
            return AnyView(StepHint(icon: .hand, text: "Allow Nearby devices in FlyyLink on your phone", action: ("Check again", audio.refresh)))
        case .noPhone, .checking:
            return AnyView(StepHint(icon: .wifi, text: "Waiting for your phone…", action: nil))
        default:
            return AnyView(EmptyView())
        }
    }

    private var pairDetail: AnyView {
        switch audio.stage {
        case .notPaired:
            return AnyView(StepHint(icon: .link, text: "Pair your phone with \(audio.macName)", action: ("Pair", audio.pair)))
        case .pairing:
            return AnyView(StepHint(
                icon: .hash,
                text: "Confirm the code on both screens. Nothing showing? Pick \(audio.macName) in your phone's Bluetooth settings.",
                action: ("Open Bluetooth Settings", audio.openBluetoothSettings),
                isBusy: true
            ))
        case .failed(let reason) where !audio.isPaired:
            return AnyView(StepHint(icon: .triangleAlert, text: reason, action: ("Try again", audio.pair), isWarning: true))
        default:
            return AnyView(EmptyView())
        }
    }

    private var connectDetail: AnyView {
        switch audio.stage {
        case .connecting:
            return AnyView(StepHint(icon: .radio, text: "Connecting to \(audio.phoneName ?? "your phone")…", action: nil, isBusy: true))
        case .paused:
            return AnyView(StepHint(icon: .circlePause, text: "You're paired. Now turn on “Use this Mac for calls”.", action: nil))
        case .ready:
            return AnyView(StepHint(icon: .badgeCheck, text: "Calls play on this Mac", action: nil))
        case .failed(let reason) where audio.isPaired:
            return AnyView(StepHint(icon: .triangleAlert, text: reason, action: ("Try again", audio.retry), isWarning: true))
        default:
            return AnyView(EmptyView())
        }
    }

    private var footer: some View {
        VStack(spacing: 12) {
            Toggle(isOn: $audio.isEnabled) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Use this Mac for calls").font(.system(size: 13, weight: .semibold))
                    Text("When it's off, calls stay on your phone")
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                }
            }
            .toggleStyle(.switch)
            .card(padding: 12)

            Text("The sound is like a headset's. It can be unreliable on some versions of macOS.")
                .font(.system(size: 10.5))
                .foregroundStyle(.tertiary)
                .multilineTextAlignment(.center)
        }
    }
}

/// A phone and a Mac with sound flowing between them. The waves move while calls are connected.
private struct CallAudioHero: View {
    let isLive: Bool

    var body: some View {
        HStack(spacing: 18) {
            glyph(.smartphone, size: 34)
            Waves(isLive: isLive)
                .frame(width: 96, height: 34)
            glyph(.laptop, size: 40)
        }
        .padding(.vertical, 18)
        .padding(.horizontal, 26)
        .background(
            RoundedRectangle(cornerRadius: 26, style: .continuous)
                .fill(LinearGradient(colors: [Theme.tint, Theme.tint.opacity(0.4)], startPoint: .topLeading, endPoint: .bottomTrailing))
        )
        .overlay(RoundedRectangle(cornerRadius: 26, style: .continuous).stroke(Theme.border))
    }

    private func glyph(_ icon: Lucide, size: CGFloat) -> some View {
        Image(lucide: icon, size: size, weight: .regular)
            .foregroundStyle(Theme.accent)
            .frame(width: 64, height: 64)
    }
}

private struct Waves: View {
    let isLive: Bool

    var body: some View {
        TimelineView(.animation(paused: !isLive)) { context in
            let time = context.date.timeIntervalSinceReferenceDate
            HStack(alignment: .center, spacing: 4) {
                ForEach(0..<11, id: \.self) { index in
                    let phase = sin(time * 4 + Double(index) * 0.7)
                    let height = isLive ? 8 + 22 * abs(phase) : 6
                    Capsule()
                        .fill(isLive ? Theme.accent : Theme.dash)
                        .frame(width: 4, height: height)
                }
            }
            .frame(maxHeight: .infinity)
        }
    }
}

struct ExperimentalBadge: View {
    var body: some View {
        Text("EXPERIMENTAL")
            .font(.system(size: 9, weight: .bold))
            .tracking(0.6)
            .foregroundStyle(Theme.warn)
            .padding(.horizontal, 6)
            .padding(.vertical, 3)
            .background(Capsule().fill(Theme.warn.opacity(0.14)))
    }
}

/// One numbered step: a circle that turns into a check when done, and a hint while it is the current step.
private enum StepState {
    case pending
    case active
    case done
}

private struct SetupStep<Detail: View>: View {
    let number: Int
    let title: String
    let state: StepState
    @ViewBuilder let detail: () -> Detail

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            ZStack {
                Circle().fill(circleFill).frame(width: 26, height: 26)
                if state == .done {
                    Image(lucide: .check, size: 11, weight: .bold).foregroundStyle(.white)
                } else {
                    Text("\(number)").font(.system(size: 12, weight: .bold)).foregroundStyle(state == .active ? Color.white : Color.secondary)
                }
            }

            VStack(alignment: .leading, spacing: 8) {
                Text(title)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(state == .pending ? .secondary : .primary)
                    .padding(.top, 4)
                if state == .active {
                    detail().transition(.opacity.combined(with: .move(edge: .top)))
                }
            }

            Spacer(minLength: 0)
        }
        .card(padding: 12)
        .opacity(state == .pending ? 0.65 : 1)
    }

    private var circleFill: Color {
        switch state {
        case .pending: return Theme.thumb
        case .active: return Theme.accent
        case .done: return Theme.ok
        }
    }
}

private struct StepHint: View {
    let icon: Lucide
    let text: String
    let action: (String, () -> Void)?
    var isBusy = false
    var isWarning = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top, spacing: 8) {
                if isBusy {
                    ProgressView().controlSize(.small)
                } else {
                    Image(lucide: icon, size: 12, weight: .semibold)
                        .foregroundStyle(isWarning ? Theme.warn : Theme.accent)
                }
                Text(text)
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            if let action {
                Button(action.0, action: action.1)
                    .buttonStyle(.borderedProminent)
                    .controlSize(.small)
            }
        }
    }
}

/// The panel row for call audio: where it stands, with a switch once it's set up.
struct CallAudioRow: View {
    @ObservedObject var audio: HandsFreeAudio
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        HStack(spacing: 10) {
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .fill(audio.isReady ? Theme.okTint : Theme.tint)
                .frame(width: 34, height: 34)
                .overlay(
                    Image(lucide: audio.audioOnMac ? .audioLines : .headphones, size: 14, weight: .semibold)
                        .foregroundStyle(audio.isReady ? Theme.ok : Theme.accent)
                )

            VStack(alignment: .leading, spacing: 1) {
                HStack(spacing: 6) {
                    Text("Call audio").font(.system(size: 13, weight: .semibold))
                    ExperimentalBadge()
                }
                Text(subtitle)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }

            Spacer(minLength: 0)

            if audio.isReady || audio.stage == .paused || audio.stage == .connecting {
                Toggle("", isOn: $audio.isEnabled)
                    .toggleStyle(.switch)
                    .controlSize(.small)
                    .labelsHidden()
            } else {
                Button("Set up", action: openSetup)
                    .buttonStyle(.borderedProminent)
                    .controlSize(.small)
            }
        }
        .contentShape(Rectangle())
        .onTapGesture(perform: openSetup)
        .card(padding: 10)
    }

    private var subtitle: String {
        switch audio.stage {
        case .ready: return audio.audioOnMac ? "Playing on this Mac" : "Calls play on this Mac"
        case .connecting: return "Connecting over Bluetooth…"
        case .paused: return "Off, so calls stay on your phone"
        case .pairing: return "Confirm the code on both screens"
        case .failed: return "Something needs your attention"
        default: return "Hear and answer calls here"
        }
    }

    private func openSetup() {
        NSApplication.shared.activate(ignoringOtherApps: true)
        openWindow(id: FlyyLinkApp.callAudioWindow)
    }
}
