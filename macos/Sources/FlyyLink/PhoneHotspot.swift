import FlyyLinkKit
import CoreWLAN
import Foundation
import OSLog
import SystemConfiguration

/// The phone's own hotspot. FlyyLink learns its name the first time this Mac is on it, then can join it
/// again on request once the phone has turned it on. macOS keeps the password, so we never see it.
final class PhoneHotspot: @unchecked Sendable {
    /// What a join is doing, for the panel.
    enum Status: Equatable, Sendable {
        case waiting(ssid: String)
        case joining(ssid: String)
        case failed(String)
    }

    private static let pollEvery: TimeInterval = 3
    private static let giveUpAfter: TimeInterval = 2 * 60

    private let defaults: UserDefaults
    private let queue = DispatchQueue(label: "com.toneflix.flyylink.hotspot")
    private let log = Logger(subsystem: "com.toneflix.flyylink", category: "hotspot")
    private var attempt = 0

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    /// The hotspot name of each phone, by device id.
    private var names: [String: String] {
        get { defaults.dictionary(forKey: "phoneHotspots") as? [String: String] ?? [:] }
        set { defaults.set(newValue, forKey: "phoneHotspots") }
    }

    func name(of deviceId: String) -> String? { names[deviceId] }

    /// Remembers the Wi-Fi this Mac is on as the phone's hotspot when the phone is its router.
    /// Returns false when the name is hidden because FlyyLink has no location access.
    @discardableResult
    func learn(deviceId: String, phoneHost: String) -> Bool {
        let host = phoneHost.hasPrefix("::ffff:") ? String(phoneHost.dropFirst(7)) : phoneHost
        guard Self.router() == host else { return true }
        guard let ssid = CWWiFiClient.shared().interface()?.ssid() else { return false }

        if names[deviceId] != ssid {
            names[deviceId] = ssid
            log.info("Learned the phone's hotspot \(ssid, privacy: .public)")
        }

        return true
    }

    /// Waits for the phone's hotspot to show up, then joins it. `onStatus` is called on a background queue
    /// and ends with nil when joined.
    func join(deviceId: String, onStatus: @escaping (Status?) -> Void) {
        guard let ssid = names[deviceId] else { return }

        queue.async { [self] in
            attempt += 1
            poll(ssid: ssid, attempt: attempt, until: Date().addingTimeInterval(Self.giveUpAfter), onStatus: onStatus)
        }
    }

    func cancel() {
        queue.async { self.attempt += 1 }
    }

    private func poll(ssid: String, attempt: Int, until deadline: Date, onStatus: @escaping (Status?) -> Void) {
        guard attempt == self.attempt else { return }

        guard let wifi = CWWiFiClient.shared().interface(), let device = wifi.interfaceName else {
            onStatus(.failed("This Mac doesn't have Wi-Fi"))
            return
        }

        if wifi.ssid() == ssid {
            onStatus(nil)
            return
        }

        let visible = (try? wifi.scanForNetworks(withName: ssid))?.isEmpty == false
        guard visible else {
            guard Date() < deadline else {
                onStatus(.failed("Couldn't find \(ssid). Turn on your phone's hotspot and try again."))
                return
            }

            onStatus(.waiting(ssid: ssid))
            queue.asyncAfter(deadline: .now() + Self.pollEvery) { [weak self] in
                self?.poll(ssid: ssid, attempt: attempt, until: deadline, onStatus: onStatus)
            }
            return
        }

        onStatus(.joining(ssid: ssid))
        do {
            try Self.associate(device: device, ssid: ssid)
            onStatus(nil)
        } catch {
            log.error("Couldn't join \(ssid, privacy: .public): \(String(describing: error), privacy: .public)")
            onStatus(.failed("Couldn't join \(ssid). Pick it once in the Wi-Fi menu."))
        }
    }

    /// Joins a network macOS already knows, using the password in the system keychain.
    private static func associate(device: String, ssid: String) throws {
        let process = Process()
        let output = Pipe()
        process.executableURL = URL(fileURLWithPath: "/usr/sbin/networksetup")
        process.arguments = ["-setairportnetwork", device, ssid]
        process.standardOutput = output
        process.standardError = output

        try process.run()
        let said = String(data: output.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8)?
            .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        process.waitUntilExit()

        // It exits 0 even when it fails, and says why instead.
        guard process.terminationStatus == 0, said.isEmpty else { throw ProtocolError(said.isEmpty ? "networksetup failed" : said) }
    }

    /// The IPv4 router of this Mac's main network.
    private static func router() -> String? {
        let store = SCDynamicStoreCreate(nil, "FlyyLink" as CFString, nil, nil)
        let global = SCDynamicStoreCopyValue(store, "State:/Network/Global/IPv4" as CFString) as? [String: Any]

        return global?["Router"] as? String
    }
}

