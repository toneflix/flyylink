import Foundation

/// One SIM's mobile signal: `networkType` as KDE Connect names it ("LTE", "5G", "UMTS"...) and `strength` from 0 to 4 bars.
/// Mirrors Connectivity.kt.
public struct CellSignal: Equatable, Sendable {
    public var networkType: String
    public var strength: Int

    public static let maxStrength = 4

    public static let nr = "5G"
    public static let lte = "LTE"
    public static let hspa = "HSPA"
    public static let umts = "UMTS"
    public static let cdma2000 = "CDMA2000"
    public static let edge = "EDGE"
    public static let gprs = "GPRS"
    public static let gsm = "GSM"
    public static let cdma = "CDMA"
    public static let iden = "iDEN"
    public static let unknown = "Unknown"

    public init(networkType: String, strength: Int) {
        self.networkType = networkType
        self.strength = strength
    }

    /// The short name a status bar shows, such as "4G", or "" when unknown.
    public var generation: String {
        switch networkType {
        case Self.nr: return "5G"
        case Self.lte: return "4G"
        case Self.hspa: return "H"
        case Self.umts, Self.cdma2000: return "3G"
        case Self.edge: return "E"
        case Self.gprs, Self.gsm, Self.cdma, Self.iden: return "2G"
        default: return ""
        }
    }
}

/// The phone's mobile signal for each SIM ("kdeconnect.connectivity_report"), keyed by subscription id.
public struct ConnectivityReport: Equatable, Sendable {
    public var signals: [String: CellSignal]

    public init(signals: [String: CellSignal]) {
        self.signals = signals
    }

    /// The SIM a computer shows: the one with the most bars.
    public var best: CellSignal? {
        signals.sorted { $0.key < $1.key }.map(\.value).max { $0.strength < $1.strength }
    }

    public static func request() -> NetworkPacket {
        NetworkPacket(type: PacketType.connectivityReportRequest)
    }

    public init?(packet: NetworkPacket) {
        guard packet.type == PacketType.connectivityReport else { return nil }

        guard case .object(let strengths)? = packet.body["signalStrengths"] else {
            self.init(signals: [:])
            return
        }

        var signals: [String: CellSignal] = [:]
        for (id, value) in strengths {
            guard case .object(let entry) = value, let strength = entry["signalStrength"]?.intValue else { continue }

            let type = entry["networkType"]?.stringValue ?? CellSignal.unknown
            signals[id] = CellSignal(networkType: type, strength: min(max(Int(strength), 0), CellSignal.maxStrength))
        }

        self.init(signals: signals)
    }

    public func toPacket() -> NetworkPacket {
        let strengths = signals.mapValues { signal -> JSONValue in
            .object(["networkType": .string(signal.networkType), "signalStrength": .int(Int64(signal.strength))])
        }

        return NetworkPacket(type: PacketType.connectivityReport, body: ["signalStrengths": .object(strengths)])
    }
}
