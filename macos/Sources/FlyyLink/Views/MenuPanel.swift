import AppKit
import SwiftUI
import FlyyLinkKit

/// The window that opens from the menu bar icon.
struct MenuPanel: View {
    @ObservedObject var model: AppModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            PanelHeader(model: model)

            if model.licence.showsCard {
                LicenceCard(licence: model.licence)
                    .transition(.scale(scale: 0.96).combined(with: .opacity))
            } else {
                if model.licence.showsTrialBanner {
                    TrialBanner(licence: model.licence)
                }
                content
            }

            PanelFooter(name: model.identity.deviceName)
        }
        .padding(14)
        .frame(width: Theme.panelWidth)
        .glassBackdrop()
        .animation(.spring(response: 0.35, dampingFraction: 0.85), value: model.devices)
        .animation(.easeOut(duration: 0.2), value: model.recent)
        .animation(.spring(response: 0.35, dampingFraction: 0.85), value: model.flyyDropAsks)
        .animation(.spring(response: 0.35, dampingFraction: 0.85), value: model.licence.showsCard)
        .fitsWindowHeight()
    }

    @ViewBuilder private var content: some View {
        if let error = model.error {
            Banner(icon: .triangleAlert, text: error)
        }

        if model.localNetworkBlocked {
            Banner(
                icon: .wifi,
                text: "Turn on FlyyLink under Local Network so your devices can find this Mac.",
                action: ("Open Settings", model.openLocalNetworkSettings)
            )
        }

        ForEach(model.flyyDropAsks.prefix(1)) { ask in
            FlyyDropAskCard(ask: ask, model: model)
                .transition(.scale(scale: 0.96).combined(with: .opacity))
        }

        main

        PlayOnTvCard(model: model.playOnTv)

        if model.flyyDropReceiving != .off {
            FlyyDropReceivingBanner(model: model)
                .transition(.opacity)
        }

        let others = model.devices.filter { $0.pairState != .paired && $0.id != focused?.id }
        if !others.isEmpty {
            NearbyList(devices: others, model: model)
        }

        if !model.recent.isEmpty {
            RecentList(items: model.recent) { model.clearRecent() }
        }

        if !model.notificationsAvailable {
            Banner(icon: .bellOff, text: "Notifications need the app bundle. Build it with macos/scripts/bundle.sh.")
        }
    }

    /// The device the big cards are about: one asking to pair first, then the first paired one.
    private var focused: DeviceState? {
        model.devices.first { $0.pairState == .requestedByPeer || $0.pairState == .requested }
            ?? model.devices.first { $0.id == model.primaryId }
    }

    @ViewBuilder private var main: some View {
        if let device = focused, device.pairState != .paired {
            PairingCard(device: device, model: model)
                .transition(.scale(scale: 0.96).combined(with: .opacity))
        } else if let device = focused {
            if model.switcherStyle == .carousel, let next = model.nextPhone, next.id != device.id {
                PhoneCarousel(current: device, next: next, model: model)
            } else {
                DeviceHero(device: device, model: model)
            }
            if model.finding == device.id, let location = device.location {
                FindCard(deviceName: device.name, location: location) { model.finding = nil }
                    .transition(.move(edge: .top).combined(with: .opacity))
            }
            if let media = device.media {
                NowPlayingCard(deviceId: device.id, media: media, model: model)
                    .transition(.move(edge: .top).combined(with: .opacity))
            }
            QuickActions(device: device, model: model, messages: model.messages)
            if device.isComputer {
                ScreenShareCard(device: device, model: model)
                KeyboardMouseCard(device: device, model: model)
            } else if device.isPhone {
                CallAudioRow(audio: model.callAudio)
            }
            if device.isPhone && model.sharesNotifications && !model.canReadNotifications {
                NotificationShareCard(device: device, model: model)
                    .transition(.move(edge: .top).combined(with: .opacity))
            }
            DropZone(deviceId: device.id, model: model)
        } else {
            SearchingCard(hasNearby: !model.devices.isEmpty)
                .transition(.opacity)
            if model.phoneNearby || model.hotspotStatus != nil {
                HotspotCard(model: model)
                    .transition(.move(edge: .top).combined(with: .opacity))
            }
        }
    }
}

private struct PanelHeader: View {
    @ObservedObject var model: AppModel
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        HStack(spacing: 10) {
            AppGlyph()
            Text("FlyyLink")
                .font(.system(size: 15, weight: .bold))
            Spacer()
            Menu {
                Button("Settings…", action: showSettings)
                Button("Open Downloads") { NSWorkspace.shared.open(AppModel.downloads) }
                if model.updater.isAvailable {
                    Button("Check for updates…") { model.updater.check() }
                        .disabled(!model.updater.canCheck)
                }

                Divider()
                Text("FlyyLink \(model.updater.version)")
                Button("Quit FlyyLink") { NSApplication.shared.terminate(nil) }
            } label: {
                Image(lucide: .settings, size: 14, weight: .medium)
                    .foregroundStyle(.secondary)
            }
            .menuStyle(.borderlessButton)
            .menuIndicator(.hidden)
            .fixedSize()
            .help("Settings")
        }
        .padding(.horizontal, 2)
    }

    private func showSettings() {
        NSApplication.shared.activate(ignoringOtherApps: true)
        openWindow(id: FlyyLinkApp.settingsWindow)
    }
}

/// The app icon in miniature: the logo mark on its gold tile.
struct AppGlyph: View {
    var size: CGFloat = 26

    var body: some View {
        RoundedRectangle(cornerRadius: size * 0.26, style: .continuous)
            .fill(FlyyLinkMark.tile)
            .frame(width: size, height: size)
            .overlay(
                FlyyLinkMark()
                    .fill(FlyyLinkMark.gradient)
                    .frame(width: size * 0.66, height: size * 0.66)
            )
    }
}

/// Shown while no phone is paired.
private struct SearchingCard: View {
    let hasNearby: Bool
    @State private var pulse = false

    var body: some View {
        VStack(spacing: 14) {
            ZStack {
                ForEach(0..<2) { ring in
                    Circle()
                        .stroke(Theme.accent.opacity(0.35), lineWidth: 1.5)
                        .frame(width: 64, height: 64)
                        .scaleEffect(pulse ? 1.9 : 1)
                        .opacity(pulse ? 0 : 1)
                        .animation(.easeOut(duration: 2).repeatForever(autoreverses: false).delay(Double(ring)), value: pulse)
                }
                Circle().fill(Theme.tint).frame(width: 64, height: 64)
                PhoneArt(width: 22)
            }
            .frame(height: 96)

            VStack(spacing: 5) {
                Text(hasNearby ? "Pick a device to pair" : "Looking for your devices")
                    .font(.system(size: 16, weight: .bold))
                if !hasNearby {
                    Text("Open FlyyLink on your phone or TV, on the same Wi-Fi.")
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 10)
        .card(padding: 18)
        .onAppear { pulse = true }
    }
}

/// Shown when the phone is in Bluetooth range but not on this Mac's network.
private struct HotspotCard: View {
    @ObservedObject var model: AppModel

    private var explanation: String {
        model.phoneHotspotName == nil
            ? "Not on this Wi-Fi. Join its hotspot to connect."
            : "Not on this Wi-Fi. Turn on its hotspot to connect."
    }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .fill(Theme.tint)
                .frame(width: 34, height: 34)
                .overlay(
                    Image(lucide: .wifi, size: 14, weight: .semibold)
                        .foregroundStyle(Theme.accent)
                )

            VStack(alignment: .leading, spacing: 6) {
                Text("Your phone is nearby").font(.system(size: 13, weight: .semibold))
                Text(model.hotspotStatus ?? explanation)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)

                if model.hotspotStatus == nil {
                    Button(model.phoneHotspotName.map { "Use \($0)" } ?? "Use phone's hotspot") { model.joinPhoneHotspot() }
                        .buttonStyle(.borderedProminent)
                        .controlSize(.small)
                } else {
                    Button("Cancel") { model.cancelHotspot() }
                        .buttonStyle(.bordered)
                        .controlSize(.small)
                }
            }

            Spacer(minLength: 0)
        }
        .card(padding: 12)
    }
}

/// A one-line warning card.
struct Banner: View {
    let icon: Lucide
    let text: String
    var action: (label: String, run: () -> Void)?

    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            Image(lucide: icon, size: 13).foregroundStyle(Theme.warn)
            VStack(alignment: .leading, spacing: 6) {
                Text(text)
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                if let action {
                    Button(action.label, action: action.run)
                        .buttonStyle(.plain)
                        .font(.system(size: 11, weight: .medium))
                        .foregroundStyle(Theme.accent)
                }
            }
            Spacer(minLength: 0)
        }
        .card(padding: 10)
    }
}

private struct PanelFooter: View {
    let name: String

    var body: some View {
        HStack {
            Text("This Mac: \(name)")
                .lineLimit(1)
            Spacer()
            Button("Quit") { NSApplication.shared.terminate(nil) }
                .buttonStyle(.plain)
                .foregroundStyle(Theme.accent)
        }
        .font(.system(size: 11))
        .foregroundStyle(.secondary)
        .padding(.horizontal, 4)
    }
}
