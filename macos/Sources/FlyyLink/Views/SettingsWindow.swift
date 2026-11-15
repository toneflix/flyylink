import AppKit
import SwiftUI
import FlyyLinkKit

/// The pages of the Settings window, in sidebar order.
enum SettingsSection: String, CaseIterable, Identifiable {
    case general
    case notifications
    case sharing
    case devices
    case about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .general: return "General"
        case .notifications: return "Notifications"
        case .sharing: return "Sharing"
        case .devices: return "Devices"
        case .about: return "About"
        }
    }

    var icon: Lucide {
        switch self {
        case .general: return .settings
        case .notifications: return .bell
        case .sharing: return .send
        case .devices: return .smartphone
        case .about: return .badgeCheck
        }
    }
}

/// FlyyLink's settings: a sidebar of sections and the chosen section's controls.
struct SettingsWindow: View {
    @ObservedObject var model: AppModel
    @AppStorage("settingsSection") private var section = SettingsSection.general

    var body: some View {
        HStack(spacing: 0) {
            sidebar
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    Text(section.title).font(.system(size: 22, weight: .bold))
                    page
                }
                .padding(24)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 680, height: 480)
        .glassBackdrop()
        .animation(.easeOut(duration: 0.15), value: section)
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 8) {
                AppGlyph(size: 22)
                Text("FlyyLink").font(.system(size: 14, weight: .bold))
            }
            .padding(.horizontal, 8)
            .padding(.bottom, 14)

            ForEach(SettingsSection.allCases) { item in
                SidebarRow(section: item, isSelected: item == section) { section = item }
            }

            Spacer()
        }
        .padding(.horizontal, 10)
        .padding(.top, 18)
        .frame(width: 190)
    }

    @ViewBuilder private var page: some View {
        switch section {
        case .general: GeneralSettings(model: model)
        case .notifications: NotificationSettings(model: model)
        case .sharing: SharingSettings(model: model)
        case .devices: DeviceSettings(model: model)
        case .about: AboutSettings(model: model, licence: model.licence, updater: model.updater)
        }
    }
}

private struct SidebarRow: View {
    let section: SettingsSection
    let isSelected: Bool
    let select: () -> Void

    var body: some View {
        Button(action: select) {
            HStack(spacing: 10) {
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .fill(isSelected ? Theme.accent : Theme.tint)
                    .frame(width: 24, height: 24)
                    .overlay(
                        Image(lucide: section.icon, size: 12, weight: .semibold)
                            .foregroundStyle(isSelected ? Theme.onAccent : Theme.accent)
                    )
                Text(section.title)
                    .font(.system(size: 13, weight: isSelected ? .semibold : .regular))
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
            .background(
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(isSelected ? Theme.tint : .clear)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

// MARK: Pages

private struct GeneralSettings: View {
    @ObservedObject var model: AppModel

    var body: some View {
        SettingsGroup {
            SettingsRow(icon: .zap, title: "Open at login") {
                SettingsSwitch(isOn: $model.opensAtLogin)
            }
            RowDivider()
            SettingsRow(icon: .smartphone, title: "Switch devices with") {
                Picker("Switch devices with", selection: $model.switcherStyle) {
                    ForEach(PhoneSwitcherStyle.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
            }
        }

        SettingsGroup(title: "Keyboard shortcuts") {
            SettingsRow(icon: .keyboard, title: "Use shortcuts") {
                SettingsSwitch(isOn: $model.usesShortcuts)
            }
            RowDivider()
            ForEach(model.shortcuts.shortcuts, id: \.symbol) { shortcut in
                HStack {
                    Text(shortcut.title).font(.system(size: 12))
                    Spacer()
                    KeyCaps(symbol: shortcut.symbol)
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 7)
                .opacity(model.usesShortcuts ? 1 : 0.45)
            }
        }
    }
}

private struct NotificationSettings: View {
    @ObservedObject var model: AppModel

    var body: some View {
        SettingsGroup {
            SettingsRow(icon: .bell, title: "Show phone notifications") {
                SettingsSwitch(isOn: $model.mirrorsNotifications)
            }
            RowDivider()
            SettingsRow(icon: .laptop, title: "Share this Mac's notifications") {
                SettingsSwitch(isOn: Binding(
                    get: { model.sharesNotifications },
                    set: { model.sharesNotifications = $0 }
                ))
            }
            RowDivider()
            SettingsRow(icon: .bellRing, title: "Share phone notifications and calls with my computers") {
                SettingsSwitch(isOn: Binding(
                    get: { model.relaysPhones },
                    set: { model.relaysPhones = $0 }
                ))
            }
        }
    }
}

private struct SharingSettings: View {
    @ObservedObject var model: AppModel

    var body: some View {
        SettingsGroup(title: "FlyyDrop") {
            ForEach(FlyyDrop.Receiving.allCases, id: \.self) { mode in
                if mode != FlyyDrop.Receiving.allCases.first { RowDivider(inset: 14) }
                ChoiceRow(title: mode.title, isSelected: model.flyyDropReceiving == mode) {
                    model.setFlyyDropReceiving(mode)
                }
            }
        }

        SettingsGroup {
            SettingsRow(icon: .mousePointer2, title: "Let my devices control this Mac") {
                SettingsSwitch(isOn: Binding(
                    get: { model.allowsRemoteInput },
                    set: { model.allowsRemoteInput = $0 }
                ))
            }
            RowDivider()
            SettingsRow(icon: .download, title: "Received files", detail: "Downloads") {
                Button("Open") { NSWorkspace.shared.open(AppModel.downloads) }
                    .controlSize(.small)
            }
        }
    }
}

private struct DeviceSettings: View {
    @ObservedObject var model: AppModel

    private var paired: [DeviceState] { model.devices.filter { $0.pairState == .paired } }

    var body: some View {
        if paired.isEmpty {
            VStack(spacing: 10) {
                Image(lucide: .radar, size: 30)
                    .foregroundStyle(Theme.accent)
                Text("No paired devices").font(.system(size: 13, weight: .semibold))
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 40)
            .card()
        } else {
            SettingsGroup {
                ForEach(paired) { device in
                    if device.id != paired.first?.id { RowDivider() }
                    SettingsRow(icon: device.settingsIcon, title: device.name, detail: device.kindName) {
                        Button("Unpair") { model.unpair(device.id) }
                            .controlSize(.small)
                    }
                }
            }
        }
    }
}

private struct AboutSettings: View {
    @ObservedObject var model: AppModel
    @ObservedObject var licence: LicenceModel
    @ObservedObject var updater: AppUpdater

    var body: some View {
        HStack(spacing: 14) {
            AppGlyph(size: 52)
            VStack(alignment: .leading, spacing: 3) {
                Text("FlyyLink").font(.system(size: 17, weight: .bold))
                Text("Version \(updater.version)")
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
            }
            Spacer()
            if updater.isAvailable {
                Button("Check for updates") { updater.check() }
                    .disabled(!updater.canCheck)
            }
        }
        .card(padding: 16)

        SettingsGroup {
            if let owner = licence.licensedTo {
                SettingsRow(icon: .badgeCheck, title: "Licensed to", detail: owner) {
                    Button("Manage") { LicenceModel.manageComputers() }
                        .controlSize(.small)
                }
            } else if licence.canSignIn {
                SettingsRow(icon: .lock, title: "Licence") {
                    Button("Sign in") {
                        licence.openCard()
                        MenuBarPanel.toggle()
                    }
                    .controlSize(.small)
                }
            }
            if licence.licensedTo != nil || licence.canSignIn { RowDivider() }
            SettingsRow(icon: .laptop, title: "This Mac", detail: model.identity.deviceName) {
                EmptyView()
            }
        }

        HStack {
            Spacer()
            Button("Quit FlyyLink") { NSApplication.shared.terminate(nil) }
                .buttonStyle(.plain)
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(Theme.accent)
        }
    }
}

// MARK: Building blocks

/// A card of rows under an optional title.
private struct SettingsGroup<Content: View>: View {
    var title: String?
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let title {
                Text(title.uppercased())
                    .font(.system(size: 11, weight: .semibold))
                    .kerning(0.4)
                    .foregroundStyle(.secondary)
                    .padding(.leading, 4)
            }
            VStack(spacing: 0) { content }
                .card(padding: 0)
        }
    }
}

/// The hairline between two rows of a group.
private struct RowDivider: View {
    var inset: CGFloat = 56

    var body: some View {
        Divider().padding(.leading, inset)
    }
}

/// One setting: an icon tile, its name, an optional value under it and its control.
private struct SettingsRow<Control: View>: View {
    let icon: Lucide
    let title: String
    var detail: String?
    @ViewBuilder let control: Control

    var body: some View {
        HStack(spacing: 12) {
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(Theme.tint)
                .frame(width: 30, height: 30)
                .overlay(
                    Image(lucide: icon, size: 14, weight: .semibold)
                        .foregroundStyle(Theme.accent)
                )

            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 13, weight: .medium))
                if let detail {
                    Text(detail)
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }

            Spacer(minLength: 12)
            control
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}

/// One option of a single-choice list, ticked when chosen.
private struct ChoiceRow: View {
    let title: String
    let isSelected: Bool
    let choose: () -> Void

    var body: some View {
        Button(action: choose) {
            HStack {
                Text(title).font(.system(size: 13))
                Spacer()
                if isSelected {
                    Image(lucide: .check, size: 13, weight: .bold)
                        .foregroundStyle(Theme.accent)
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 11)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

private struct SettingsSwitch: View {
    @Binding var isOn: Bool

    var body: some View {
        Toggle("", isOn: $isOn)
            .toggleStyle(.switch)
            .labelsHidden()
            .controlSize(.small)
    }
}

/// A shortcut's keys, each on its own small key.
private struct KeyCaps: View {
    let symbol: String

    var body: some View {
        HStack(spacing: 3) {
            ForEach(Array(symbol.enumerated()), id: \.offset) { _, key in
                Text(String(key))
                    .font(.system(size: 11, weight: .medium, design: .rounded))
                    .frame(minWidth: 20, minHeight: 20)
                    .background(
                        RoundedRectangle(cornerRadius: 5, style: .continuous)
                            .fill(Theme.thumb)
                    )
            }
        }
    }
}

private extension DeviceState {
    var settingsIcon: Lucide {
        isPhone ? .smartphone : isTv ? .tv : .laptop
    }

    var kindName: String {
        isPhone ? "Phone" : isTv ? "TV" : "Computer"
    }
}

