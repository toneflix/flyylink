import SwiftUI
import FlyyLinkKit

/// The drawing for a kind of device. `width` is a phone's width; computers are drawn wider to look the same size.
struct DeviceArt: View {
    let type: DeviceType
    var width: CGFloat = 40
    var screen: Color = Theme.accent

    var body: some View {
        switch type {
        case .laptop: LaptopArt(width: width * 1.7, screen: screen)
        case .desktop: DesktopArt(width: width * 1.7, screen: screen)
        case .tablet: PhoneArt(width: width * 1.4, screen: screen)
        case .phone: PhoneArt(width: width, screen: screen)
        case .tv: TvArt(width: width * 1.9, screen: screen)
        }
    }
}

/// A drawn phone: dark body with an accent screen. Height follows the width.
struct PhoneArt: View {
    var width: CGFloat = 40
    var screen: Color = Theme.accent

    var body: some View {
        let height = width * 1.72
        let bezel = max(2, width * 0.07)

        RoundedRectangle(cornerRadius: width * 0.25, style: .continuous)
            .fill(Theme.device)
            .frame(width: width, height: height)
            .overlay(
                RoundedRectangle(cornerRadius: width * 0.25 - bezel, style: .continuous)
                    .fill(screen)
                    .padding(bezel)
            )
    }
}

/// A drawn laptop: screen on a thin base.
struct LaptopArt: View {
    var width: CGFloat = 80
    var screen: Color = Theme.screen

    var body: some View {
        VStack(spacing: 0) {
            RoundedRectangle(cornerRadius: width * 0.075, style: .continuous)
                .fill(Theme.device)
                .frame(width: width, height: width * 0.65)
                .overlay(
                    RoundedRectangle(cornerRadius: width * 0.04, style: .continuous)
                        .fill(screen)
                        .padding(max(2, width * 0.04))
                )
            Capsule()
                .fill(Color.secondary.opacity(0.45))
                .frame(width: width * 1.15, height: max(2, width * 0.075))
        }
    }
}

/// A drawn TV: a wide screen on a short stand.
struct TvArt: View {
    var width: CGFloat = 80
    var screen: Color = Theme.screen

    var body: some View {
        VStack(spacing: 0) {
            RoundedRectangle(cornerRadius: width * 0.04, style: .continuous)
                .fill(Theme.device)
                .frame(width: width, height: width * 0.58)
                .overlay(
                    RoundedRectangle(cornerRadius: width * 0.025, style: .continuous)
                        .fill(screen)
                        .padding(max(2, width * 0.025))
                )
            Rectangle()
                .fill(Theme.device)
                .frame(width: width * 0.08, height: width * 0.06)
            Capsule()
                .fill(Color.secondary.opacity(0.45))
                .frame(width: width * 0.36, height: max(2, width * 0.03))
        }
    }
}

/// A drawn desktop: a monitor on a short stand.
struct DesktopArt: View {
    var width: CGFloat = 80
    var screen: Color = Theme.screen

    var body: some View {
        VStack(spacing: 0) {
            RoundedRectangle(cornerRadius: width * 0.06, style: .continuous)
                .fill(Theme.device)
                .frame(width: width, height: width * 0.62)
                .overlay(
                    RoundedRectangle(cornerRadius: width * 0.03, style: .continuous)
                        .fill(screen)
                        .padding(max(2, width * 0.04))
                )
            Rectangle()
                .fill(Theme.device)
                .frame(width: width * 0.12, height: width * 0.12)
            Capsule()
                .fill(Color.secondary.opacity(0.45))
                .frame(width: width * 0.42, height: max(2, width * 0.06))
        }
    }
}

/// Charge as a ring, green when fine, amber when low, with a bolt while charging.
struct BatteryRing: View {
    let charge: Int?
    let isCharging: Bool
    var size: CGFloat = 40

    var body: some View {
        let fraction = Double(min(max(charge ?? 0, 0), 100)) / 100
        let isLow = (charge ?? 100) <= 15 && !isCharging
        let color = isLow ? Theme.warn : Theme.ok

        ZStack {
            Circle().stroke(Theme.track, lineWidth: 4)
            Circle()
                .trim(from: 0, to: fraction)
                .stroke(color, style: StrokeStyle(lineWidth: 4, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.easeInOut(duration: 0.6), value: fraction)

            VStack(spacing: 0) {
                Text(charge.map { "\($0)%" } ?? "–")
                    .font(.system(size: 10, weight: .bold).monospacedDigit())
                if isCharging {
                    Image(lucide: .zap, size: 7)
                        .foregroundStyle(color)
                }
            }
        }
        .frame(width: size, height: size)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(charge.map { "Battery \($0) percent\(isCharging ? ", charging" : "")" } ?? "Battery unknown")
    }
}

/// Mobile signal as four rising bars, with the network type beside them, as a phone's status bar shows it.
struct SignalBars: View {
    let signal: CellSignal
    var height: CGFloat = 11

    var body: some View {
        HStack(alignment: .bottom, spacing: 4) {
            HStack(alignment: .bottom, spacing: 1.5) {
                ForEach(1...CellSignal.maxStrength, id: \.self) { bar in
                    RoundedRectangle(cornerRadius: 1, style: .continuous)
                        .fill(bar <= signal.strength ? Color.primary.opacity(0.8) : Color.primary.opacity(0.18))
                        .frame(width: 3, height: height * CGFloat(bar) / CGFloat(CellSignal.maxStrength))
                }
            }
            .animation(.easeInOut(duration: 0.3), value: signal.strength)

            if !signal.generation.isEmpty {
                Text(signal.generation)
                    .font(.system(size: 10, weight: .bold).monospacedDigit())
                    .foregroundStyle(Color.primary.opacity(0.75))
            }
        }
        .frame(height: height)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(SignalBars.label(signal))
    }

    static func label(_ signal: CellSignal) -> String {
        guard signal.strength > 0 else { return "No signal" }

        let bars = "\(signal.strength) of \(CellSignal.maxStrength) bars"
        return signal.generation.isEmpty ? bars : "\(signal.generation), \(bars)"
    }
}
