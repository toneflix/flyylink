import SwiftUI

/// Asks for Full Disk Access, which sending this Mac's notifications to the phone needs.
struct NotificationShareCard: View {
    let device: DeviceState
    @ObservedObject var model: AppModel

    var body: some View {
        HStack(spacing: 10) {
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .fill(Theme.tint)
                .frame(width: 34, height: 34)
                .overlay(
                    Image(lucide: .bellRing, size: 14, weight: .semibold)
                        .foregroundStyle(Theme.accent)
                )

            VStack(alignment: .leading, spacing: 1) {
                Text("This Mac's notifications").font(.system(size: 13, weight: .semibold))
                Text("Get them on \(device.name)")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Spacer(minLength: 0)

            Button("Not now") { model.sharesNotifications = false }
                .controlSize(.small)
            Button("Allow") { NotificationSharePlugin.openAccessSettings() }
                .buttonStyle(.borderedProminent)
                .controlSize(.small)
        }
        .card(padding: 12)
    }
}
