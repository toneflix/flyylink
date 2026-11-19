import SwiftUI
import FlyyLinkKit

/// Other phones, tablets and computers on the network.
struct NearbyList: View {
    let devices: [DeviceState]
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            SectionTitle("Also nearby")

            ForEach(devices) { device in
                let flyyDrops = device.canFlyyDrop && device.pairState == .notPaired

                HStack(spacing: 12) {
                    Group {
                        if model.flyyDropSends[device.id]?.stage == .waiting {
                            RadarRings(size: 36) { DeviceArt(type: device.type, width: 10, screen: Theme.screen) }
                        } else {
                            DeviceArt(type: device.type, width: 20, screen: Theme.screen)
                        }
                    }
                    .frame(width: 36, height: 36)

                    VStack(alignment: .leading, spacing: 1) {
                        Text(device.name).font(.system(size: 13, weight: .semibold)).lineLimit(1)
                        Text(status(device)).font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
                    }

                    Spacer(minLength: 0)

                    if flyyDrops {
                        FlyyDropButton(device: device, model: model)
                    }
                    action(device)
                }
                .card(padding: 10)
                .modifier(FlyyDropTargetIf(enabled: flyyDrops, device: device, model: model))
            }
        }
    }

    private func status(_ device: DeviceState) -> String {
        if let send = model.flyyDropSends[device.id] { return send.status }

        switch device.pairState {
        case .notPaired: return device.canFlyyDrop ? "Not paired yet. Drop files to send." : "Not paired"
        case .requested: return "Waiting for it to accept"
        case .requestedByPeer: return "Wants to pair next"
        case .paired: return device.battery.map { "Paired · \($0.charge)%" } ?? "Paired"
        }
    }

    @ViewBuilder private func action(_ device: DeviceState) -> some View {
        switch device.pairState {
        case .notPaired:
            SmallButton(title: "Pair") { model.requestPair(device.id) }
        case .requestedByPeer:
            SmallButton(title: "Not now") { model.reject(device.id) }
        case .paired:
            SmallButton(title: "Show") { model.selectedPhone = device.id }
        case .requested:
            ProgressView().controlSize(.small)
        }
    }
}

struct SmallButton: View {
    let title: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(Theme.accentStrong)
                .padding(.horizontal, 12)
                .frame(height: 28)
                .background(Theme.tint, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        }
        .buttonStyle(PressStyle())
    }
}

/// Small uppercase heading above a list.
struct SectionTitle: View {
    let text: String

    init(_ text: String) {
        self.text = text
    }

    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 11, weight: .semibold))
            .tracking(0.6)
            .foregroundStyle(.secondary)
            .padding(.horizontal, 4)
    }
}

/// Makes a row a FlyyDrop target only for devices that take FlyyDrops.
private struct FlyyDropTargetIf: ViewModifier {
    let enabled: Bool
    let device: DeviceState
    @ObservedObject var model: AppModel

    @ViewBuilder
    func body(content: Content) -> some View {
        if enabled {
            content.modifier(FlyyDropTarget(device: device, model: model))
        } else {
            content
        }
    }
}
