import SwiftUI
import UniformTypeIdentifiers
import FlyyLinkKit

/// Someone nearby wants to FlyyDrop: who, what, and Accept or Decline.
struct FlyyDropAskCard: View {
    let ask: FlyyDropAsk
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(spacing: 16) {
            RadarRings(size: 112) {
                DeviceArt(type: ask.deviceType, width: 22, screen: Theme.screen)
            }

            VStack(spacing: 6) {
                Text("\(ask.deviceName) wants to FlyyDrop")
                    .font(.system(size: 17, weight: .bold))
                    .multilineTextAlignment(.center)
                if !ask.isPaired {
                    Text("Not paired")
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                }
            }

            HStack(spacing: 12) {
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(Theme.tint)
                    .frame(width: 40, height: 40)
                    .overlay(
                        Image(lucide: icon, size: 16, weight: .semibold)
                            .foregroundStyle(Theme.accent)
                    )

                VStack(alignment: .leading, spacing: 2) {
                    Text(ask.summary)
                        .font(.system(size: 13, weight: .semibold))
                        .lineLimit(2)
                    if let detail {
                        Text(detail)
                            .font(.system(size: 11))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }

                Spacer(minLength: 0)
            }
            .padding(10)
            .background(Theme.thumb.opacity(0.6), in: RoundedRectangle(cornerRadius: 12, style: .continuous))

            HStack(spacing: 8) {
                Button("Decline") { model.declineFlyyDrop(ask.id) }
                    .buttonStyle(PillStyle(prominent: false))
                Button("Accept") { model.acceptFlyyDrop(ask.id) }
                    .buttonStyle(PillStyle(prominent: true))
                    .keyboardShortcut(.defaultAction)
            }
        }
        .frame(maxWidth: .infinity)
        .card(padding: 20)
    }

    private var icon: Lucide {
        if ask.offer.items.isEmpty { return .link }

        return ask.isImages ? .image : .fileText
    }

    private var detail: String? {
        let items = ask.offer.items
        if items.isEmpty { return "Link or text" }
        guard items.count > 1 else { return nil }

        return items.prefix(3).map(\.safeName).joined(separator: ", ")
    }
}

/// Rings that pulse out from `content`, like a device being found nearby.
struct RadarRings<Content: View>: View {
    let size: CGFloat
    @ViewBuilder let content: Content
    @State private var pulse = false

    var body: some View {
        ZStack {
            ForEach(0..<3) { ring in
                Circle()
                    .stroke(Theme.accent.opacity(0.4), lineWidth: 1.5)
                    .frame(width: size * 0.5, height: size * 0.5)
                    .scaleEffect(pulse ? 2 : 1)
                    .opacity(pulse ? 0 : 1)
                    .animation(.easeOut(duration: 2.4).repeatForever(autoreverses: false).delay(Double(ring) * 0.8), value: pulse)
            }
            Circle()
                .fill(Theme.tint)
                .frame(width: size * 0.5, height: size * 0.5)
            content
        }
        .frame(width: size, height: size)
        .onAppear { pulse = true }
    }
}

/// The FlyyDrop part of a nearby device's row: a button and drop target that asks it to take files,
/// then how that is going.
struct FlyyDropButton: View {
    let device: DeviceState
    @ObservedObject var model: AppModel

    private var send: FlyyDropSend? { model.flyyDropSends[device.id] }

    var body: some View {
        Group {
            switch send?.stage {
            case .waiting:
                Button { model.cancelFlyyDrop(to: device.id) } label: {
                    Image(lucide: .x, size: 12, weight: .semibold)
                        .foregroundStyle(.secondary)
                        .frame(width: 28, height: 28)
                        .background(Theme.thumb, in: Circle())
                }
                .buttonStyle(PressStyle())
                .help("Stop asking")
            case .sending:
                ProgressRing(fraction: send?.progress?.fraction ?? 0)
            case .sent:
                Image(lucide: .circleCheck, size: 16, weight: .semibold).foregroundStyle(Theme.ok)
            case .declined, .noAnswer, .failed:
                Image(lucide: .circleX, size: 16, weight: .semibold).foregroundStyle(Theme.warn)
            case nil:
                Button { model.chooseFilesToFlyyDrop(for: device.id) } label: {
                    Image(lucide: .radar, size: 14, weight: .semibold)
                        .foregroundStyle(Theme.accentStrong)
                        .frame(width: 30, height: 28)
                        .background(Theme.tint, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                }
                .buttonStyle(PressStyle())
                .help("FlyyDrop files to \(device.name)")
            }
        }
        .animation(.spring(response: 0.3, dampingFraction: 0.8), value: send?.stage)
    }
}

/// Lets files be dropped on a nearby device's row to FlyyDrop them.
struct FlyyDropTarget: ViewModifier {
    let device: DeviceState
    @ObservedObject var model: AppModel

    @State private var targeted = false

    func body(content: Content) -> some View {
        content
            .overlay(
                RoundedRectangle(cornerRadius: Theme.radius, style: .continuous)
                    .stroke(Theme.accent, style: StrokeStyle(lineWidth: 1.5, dash: [5, 4]))
                    .opacity(targeted ? 1 : 0)
            )
            .scaleEffect(targeted ? 1.02 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: targeted)
            .onDrop(of: [.fileURL], isTargeted: $targeted) { providers in
                DropZone.loadURLs(providers) { urls in
                    Task { @MainActor in model.flyyDrop(urls, to: device.id) }
                }
                return true
            }
    }
}

/// How far a send has got, as a small ring.
private struct ProgressRing: View {
    let fraction: Double

    var body: some View {
        ZStack {
            Circle().stroke(Theme.track, lineWidth: 3)
            Circle()
                .trim(from: 0, to: fraction)
                .stroke(Theme.accent, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
        .frame(width: 20, height: 20)
        .animation(.easeOut(duration: 0.2), value: fraction)
    }
}

extension FlyyDrop.Receiving {
    var title: String {
        switch self {
        case .off: return "Paired devices only"
        case .everyoneFor10Minutes: return "Everyone for 10 minutes"
        case .everyone: return "Everyone"
        }
    }

    /// A short line for the panel, with the minutes left for the timed setting.
    func caption(until: Date?) -> String {
        switch self {
        case .off: return "Paired devices only"
        case .everyone: return "Open to everyone nearby"
        case .everyoneFor10Minutes:
            let minutes = max(1, Int(((until ?? Date()).timeIntervalSinceNow / 60).rounded(.up)))
            return "Open to everyone · \(minutes) min left"
        }
    }
}

/// The gear menu entry for who may FlyyDrop to this Mac.
struct FlyyDropReceivingMenu: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Picker("Who can FlyyDrop to this Mac", selection: Binding(
            get: { model.flyyDropReceiving },
            set: { model.setFlyyDropReceiving($0) }
        )) {
            ForEach(FlyyDrop.Receiving.allCases, id: \.self) { mode in
                Text(mode.title).tag(mode)
            }
        }
    }
}

/// Shown while anyone nearby may FlyyDrop to this Mac, with a way to stop that.
struct FlyyDropReceivingBanner: View {
    @ObservedObject var model: AppModel

    var body: some View {
        HStack(spacing: 10) {
            Image(lucide: .radar, size: 13, weight: .semibold)
                .foregroundStyle(Theme.accent)
            TimelineView(.periodic(from: .now, by: 30)) { _ in
                Text(model.flyyDropReceiving.caption(until: model.flyyDropUntil))
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            Spacer(minLength: 0)
            Button("Turn off") { model.setFlyyDropReceiving(.off) }
                .buttonStyle(.plain)
                .font(.system(size: 11, weight: .medium))
                .foregroundStyle(Theme.accent)
        }
        .card(padding: 10)
    }
}
