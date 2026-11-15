import Foundation
import FlyyLinkKit

/// Keeps each phone's mobile signal for the panel.
final class ConnectivityPlugin: Plugin {
    let incoming: Set<String> = [PacketType.connectivityReport]

    private var reports: [String: ConnectivityReport] = [:]
    private let lock = NSLock()

    /// Called on a networking thread when a phone's signal changes. Set it before the link starts.
    var onChange: (() -> Void)?

    func signal(for deviceId: String) -> CellSignal? {
        lock.withLock { reports[deviceId]?.best }
    }

    func onPaired(_ device: Device) {
        device.send(ConnectivityReport.request())
    }

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        guard let report = ConnectivityReport(packet: packet) else { return }

        let changed = lock.withLock { reports.updateValue(report, forKey: device.id) != report }
        if changed { onChange?() }
    }
}
