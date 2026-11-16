import Foundation

/// A phone feature the user turns on in the app. `sends` are the packets the phone sends for it and
/// `accepts` the ones it takes from computers, so neither goes through while the feature is off. Mirrors Features.kt.
public enum PhoneFeature: String, CaseIterable, Sendable {
    case notifications, media, messages, clipboard, photos, files, mirroring, control, location

    public var sends: Set<String> {
        switch self {
        case .notifications: return [PacketType.notification]
        case .media: return [PacketType.mpris]
        case .messages: return [PacketType.smsMessages, PacketType.telephony, PacketType.contacts, PacketType.callAudio]
        case .clipboard: return [PacketType.clipboard, PacketType.clipboardConnect, PacketType.clipboardFile]
        case .photos: return [PacketType.photos, PacketType.photosFile]
        case .files: return [PacketType.files]
        case .mirroring: return [PacketType.screen]
        case .control: return []
        case .location: return [PacketType.location]
        }
    }

    public var accepts: Set<String> {
        switch self {
        case .notifications: return [PacketType.notificationRequest, PacketType.notificationReply, PacketType.notificationAction]
        case .media: return [PacketType.mprisRequest]
        case .messages:
            return [
                PacketType.smsRequest,
                PacketType.smsRequestConversations,
                PacketType.smsRequestConversation,
                PacketType.callRequest,
                PacketType.contactsRequest,
                PacketType.callAudioRequest,
            ]
        case .clipboard: return [PacketType.clipboard, PacketType.clipboardConnect, PacketType.clipboardFile]
        case .photos: return [PacketType.photosRequest, PacketType.photosDownload]
        case .files: return [PacketType.filesRequest]
        case .mirroring: return [PacketType.screenRequest]
        case .control: return [PacketType.screenInput]
        case .location: return [PacketType.locationRequest]
        }
    }
}

/// The features a device has on. `reported` is nil when the device doesn't say, as computers, TVs and
/// older phones don't, and then every feature counts as on.
public struct PhoneFeatures: Equatable, Sendable {
    public static let bodyKey = "features"

    public static let all = PhoneFeatures(reported: nil)

    public let reported: Set<PhoneFeature>?

    public init(reported: Set<PhoneFeature>?) {
        self.reported = reported
    }

    public static func of(_ features: PhoneFeature...) -> PhoneFeatures {
        PhoneFeatures(reported: Set(features))
    }

    /// Reads the list in an identity or `PacketType.features` packet. Unknown names are skipped.
    public init(body packet: NetworkPacket) {
        guard packet.body[Self.bodyKey] != nil else {
            self.init(reported: nil)
            return
        }

        self.init(reported: Set(packet.stringList(Self.bodyKey).compactMap(PhoneFeature.init(rawValue:))))
    }

    public init?(packet: NetworkPacket) {
        guard packet.type == PacketType.features, packet.body[Self.bodyKey] != nil else { return nil }

        self.init(body: packet)
    }

    public func contains(_ feature: PhoneFeature) -> Bool {
        reported?.contains(feature) ?? true
    }

    /// False when `type` belongs to a feature that is off on the sending side.
    public func sends(_ type: String) -> Bool {
        !PhoneFeature.allCases.contains { $0.sends.contains(type) && !contains($0) }
    }

    /// False when `type` belongs to a feature that is off on the receiving side.
    public func accepts(_ type: String) -> Bool {
        !PhoneFeature.allCases.contains { $0.accepts.contains(type) && !contains($0) }
    }

    /// The wire names of the features that are on, for the identity and `toPacket()`; nil when not reported.
    public var wire: [String]? {
        reported?.map(\.rawValue).sorted()
    }

    /// Tells paired devices which features are on, after one changes.
    public func toPacket() -> NetworkPacket {
        NetworkPacket(type: PacketType.features, body: [Self.bodyKey: .array((wire ?? []).map { .string($0) })])
    }
}
