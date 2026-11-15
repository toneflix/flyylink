import SwiftUI
import FlyyLinkKit

/// The paired phone or computer at the top of the panel, on the Toneflix gold: picture, name, connection, signal and battery.
struct DeviceHero: View {
    let device: DeviceState
    @ObservedObject var model: AppModel

    var body: some View {
        HStack(spacing: 14) {
            DeviceArt(type: device.type, width: device.isComputer ? 30 : 40)

            VStack(alignment: .leading, spacing: 6) {
                DeviceNameMenu(device: device, model: model)

                HStack(spacing: 6) {
                    HStack(spacing: 6) {
                        Circle().fill(Theme.ok).frame(width: 7, height: 7)
                        Text("Connected")
                        Image(lucide: .wifi, size: 11, weight: .semibold)
                    }
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(Theme.ok)
                    .padding(.horizontal, 9)
                    .padding(.vertical, 4)
                    .background(Theme.okTint, in: Capsule())
                    .help("Connected over Wi-Fi")
                    .accessibilityElement(children: .combine)
                    .accessibilityLabel("Connected over Wi-Fi")

                    if let signal = device.signal {
                        SignalBars(signal: signal)
                            .padding(.horizontal, 9)
                            .padding(.vertical, 5)
                            .background(Color.black.opacity(0.08), in: Capsule())
                            .help(SignalBars.label(signal))
                    }
                }

                HStack(spacing: 6) {
                    Button { model.mirrorsNotifications.toggle() } label: {
                        StatusIcon(icon: model.mirrorsNotifications ? .bell : .bellOff, isOn: model.mirrorsNotifications)
                    }
                    .buttonStyle(.plain)
                    .help(model.mirrorsNotifications ? "Pause notifications" : "Turn on notifications")
                    .accessibilityLabel(model.mirrorsNotifications ? "Notifications on" : "Notifications paused")

                    StatusIcon(icon: .clipboard, isOn: true)
                        .help("Clipboard synced")
                        .accessibilityLabel("Clipboard synced")
                }
            }

            Spacer(minLength: 0)

            if device.isPhone || device.battery != nil {
                BatteryRing(charge: device.battery?.charge, isCharging: device.battery?.isCharging ?? false)
            }
        }
        .padding(16)
        .environment(\.colorScheme, .light)
        .background(Theme.gold, in: RoundedRectangle(cornerRadius: Theme.radius, style: .continuous))
    }
}

/// A small round badge in the hero for one thing that is on or off, such as notifications.
private struct StatusIcon: View {
    let icon: Lucide
    let isOn: Bool

    var body: some View {
        Image(lucide: icon, size: 12, weight: .semibold)
            .foregroundStyle(isOn ? Color.primary.opacity(0.75) : Color.secondary.opacity(0.6))
            .frame(width: 24, height: 24)
            .background(Color.black.opacity(isOn ? 0.08 : 0.04), in: Circle())
            .contentShape(Circle())
    }
}
