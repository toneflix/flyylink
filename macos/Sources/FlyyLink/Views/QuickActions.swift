import AppKit
import FlyyLinkKit
import SwiftUI

/// The grid of things to do with the phone or computer the panel shows.
struct QuickActions: View {
    let device: DeviceState
    @ObservedObject var model: AppModel
    @ObservedObject var messages: MessagesStore
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        LazyVGrid(columns: [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)], spacing: 10) {
            if device.isComputer {
                computerTiles
            } else if device.isTv {
                tvTiles
            } else {
                phoneTiles
            }
        }
    }

    @ViewBuilder private var phoneTiles: some View {
        ActionTile(icon: .volume2, title: "Ring phone") {
            model.ring(device.id)
        }
        sharedTiles
        if has(.messages) {
            ActionTile(icon: .messageSquare, title: "Messages", subtitle: messagesSubtitle) {
                showMessages()
            }
            ActionTile(icon: .phone, title: "Call") {
                showMessages()
                messages.showsDialer = true
            }
        }
        if has(.mirroring) {
            ActionTile(icon: .cast, title: "Mirror") {
                model.mirror(device.id)
            }
        }
        if has(.photos) {
            ActionTile(icon: .images, title: "Photos") {
                NSApplication.shared.activate(ignoringOtherApps: true)
                openWindow(id: FlyyLinkApp.photosWindow)
            }
        }
        if model.sharesFiles(device.id) {
            ActionTile(icon: .folderOpen, title: "Files", subtitle: "In Finder") {
                model.openFiles(device.id)
            }
        }
    }

    @ViewBuilder private var computerTiles: some View {
        ActionTile(icon: .volume2, title: "Play sound") {
            model.ring(device.id)
        }
        sharedTiles
    }

    @ViewBuilder private var tvTiles: some View {
        ActionTile(icon: .gamepad2, title: "Remote") {
            model.showRemote(device.id)
        }
        ActionTile(icon: .fileUp, title: "Send file") {
            model.chooseFiles(for: device.id)
        }
        ActionTile(icon: .link, title: "Open link", subtitle: "From your clipboard") {
            if let text = NSPasteboard.general.string(forType: .string) { model.sendText(text, to: device.id) }
        }
    }

    @ViewBuilder private var sharedTiles: some View {
        ActionTile(icon: .fileUp, title: "Send file") {
            model.chooseFiles(for: device.id)
        }
        if has(.clipboard) {
            ActionTile(icon: .clipboard, title: "Clipboard", subtitle: clipboardSubtitle) {
                model.sendClipboard(to: device.id)
            }
        }
        if has(.notifications) {
            ActionTile(
                icon: model.mirrorsNotifications ? .bellRing : .bellOff,
                title: "Notifications",
                subtitle: notificationsSubtitle,
                isOn: model.mirrorsNotifications
            ) {
                model.mirrorsNotifications.toggle()
            }
        }
    }

    /// Phones show only what they have turned on; other devices have everything.
    private func has(_ feature: PhoneFeature) -> Bool {
        device.features.contains(feature)
    }

    private var messagesSubtitle: String {
        messages.unreadCount == 0 ? "Your texts" : "\(messages.unreadCount) unread"
    }

    private func showMessages() {
        NSApplication.shared.activate(ignoringOtherApps: true)
        openWindow(id: FlyyLinkApp.messagesWindow)
    }

    private var clipboardSubtitle: String {
        guard let date = model.clipboardSyncedAt else { return "Send it now" }
        return "Synced \(RelativeTime.text(date))"
    }

    private var notificationsSubtitle: String {
        guard model.mirrorsNotifications else { return "Paused" }
        return model.notificationsToday == 0 ? "On" : "\(model.notificationsToday) today"
    }
}

/// One square action: an icon chip with a title and an optional status line.
struct ActionTile: View {
    let icon: Lucide
    let title: String
    var subtitle: String? = nil
    var isOn = true
    let action: () -> Void

    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(isOn ? Theme.tint : Theme.thumb)
                    .frame(width: 34, height: 34)
                    .overlay(
                        Image(lucide: icon, size: 14, weight: .semibold)
                            .foregroundStyle(isOn ? Theme.accent : Color.secondary)
                    )

                VStack(alignment: .leading, spacing: 1) {
                    Text(title).font(.system(size: 13, weight: .semibold))
                    if let subtitle {
                        Text(subtitle)
                            .font(.system(size: 11))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                }

                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(Rectangle())
            .card(padding: 10)
            .overlay(
                RoundedRectangle(cornerRadius: Theme.radius, style: .continuous)
                    .stroke(Theme.accent.opacity(hovering ? 0.45 : 0))
            )
        }
        .buttonStyle(PressStyle())
        .onHover { hovering = $0 }
    }
}

/// Shrinks a little while pressed.
struct PressStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
    }
}

/// "2 min ago" style times.
enum RelativeTime {
    private static let formatter: RelativeDateTimeFormatter = {
        let formatter = RelativeDateTimeFormatter()
        formatter.unitsStyle = .short
        return formatter
    }()

    static func text(_ date: Date, now: Date = Date()) -> String {
        guard now.timeIntervalSince(date) >= 45 else { return "just now" }
        return formatter.localizedString(for: date, relativeTo: now)
    }
}
