import Foundation

/// KDE Connect protocol version we speak.
public let protocolVersion = 8

/// UDP port used for discovery broadcasts.
public let udpPort: UInt16 = 1716

/// TCP ports KDE Connect clients try, in order.
public let tcpPortRange: ClosedRange<UInt16> = 1716...1764

/// TCP ports a sender opens for payloads such as files.
public let payloadPortRange: ClosedRange<UInt16> = 1739...1764

/// Packet types. Standard KDE Connect ones use "kdeconnect.", our extensions use "flyylink.".
/// Keep in sync with android/protocol/.../PacketType.kt.
public enum PacketType {
    public static let identity = "kdeconnect.identity"
    public static let pair = "kdeconnect.pair"
    public static let ping = "kdeconnect.ping"
    public static let battery = "kdeconnect.battery"
    public static let batteryRequest = "kdeconnect.battery.request"
    public static let clipboard = "kdeconnect.clipboard"
    public static let clipboardConnect = "kdeconnect.clipboard.connect"
    public static let notification = "kdeconnect.notification"
    public static let notificationRequest = "kdeconnect.notification.request"
    public static let notificationReply = "kdeconnect.notification.reply"
    public static let notificationAction = "kdeconnect.notification.action"
    public static let shareRequest = "kdeconnect.share.request"
    public static let findMyPhone = "kdeconnect.findmyphone.request"
    public static let mpris = "kdeconnect.mpris"
    public static let mprisRequest = "kdeconnect.mpris.request"
    public static let smsMessages = "kdeconnect.sms.messages"
    public static let smsRequest = "kdeconnect.sms.request"
    public static let smsRequestConversations = "kdeconnect.sms.request_conversations"
    public static let smsRequestConversation = "kdeconnect.sms.request_conversation"
    public static let telephony = "kdeconnect.telephony"

    /// Asks the sender to send the rest of a file that stopped. Body: {"transferId", "offset"}.
    public static let shareResume = "flyylink.share.resume"

    /// Asks for a page of the phone's photos and videos. Body: {"before"?: ms, "count": n}.
    public static let photosRequest = "flyylink.photos.request"

    /// A page of photos with small thumbnails. Body: {"photos": [...], "more": bool}.
    public static let photos = "flyylink.photos"

    /// Asks the phone to send one photo in full: as a normal shared file, or as `photosFile` to view. Body: {"id", "preview"?}.
    public static let photosDownload = "flyylink.photos.download"

    /// One photo or video in full, for the Mac to show rather than keep. Body: {"id", "name", "mimeType"}, with a payload.
    public static let photosFile = "flyylink.photos.file"

    /// Starts or stops mirroring the phone's screen. Body: {"action": "start"|"stop", "maxSize"?, "bitrate"?}.
    public static let screenRequest = "flyylink.screen.request"

    /// The screen stream, as a payload of size -1. Body: {"width", "height", "codec", "running"}.
    public static let screen = "flyylink.screen"

    /// A tap, swipe, key or text for the mirrored screen. Body: {"action", ...}.
    public static let screenInput = "flyylink.screen.input"

    /// Asks the phone where it is. Body: {}.
    public static let locationRequest = "flyylink.location.request"

    /// Where the phone is. Body: {"latitude", "longitude", "accuracy" (metres), "time" (ms)}.
    public static let location = "flyylink.location"

    /// A copied picture or file, sent as the payload. Body: {"name": ..., "mimeType": ...}.
    public static let clipboardFile = "flyylink.clipboard.file"

    /// Answer, decline, hang up, mute or unmute a call, or dial a number. Body: {"action": ..., "number"?: ...}.
    public static let callRequest = "flyylink.call.request"

    /// Asks the phone about the Bluetooth link for call audio, or to pair with this Mac. Body: {"action": "status"|"pair", "address", "name"?}.
    public static let callAudioRequest = "flyylink.callaudio.request"

    /// The phone's side of the call audio link. Body: {"bluetoothOn", "allowed", "paired", "connected", "name"?}.
    public static let callAudio = "flyylink.callaudio"

    /// Keeps the link alive and detects a dead one. Body: {"pong": bool}.
    public static let heartbeat = "flyylink.heartbeat"

    /// Keyboard and mouse shared between computers. Body: {"action", ...}; see `RemoteInput`.
    public static let input = "flyylink.input"

    /// A phone's packet passed on through another computer. Body: {"phoneId", "phoneName", "packet"}; see `Relay`.
    public static let relay = "flyylink.relay"

    /// Asks to send files, or a link or text, to a device that may not be paired. Body: {"id", "files": [{"name", "size"}], "text"?}; see `FlyyDrop`.
    public static let flyyDropRequest = "flyylink.flyydrop.request"

    /// Accepts or declines a FlyyDrop request. Body: {"id", "accept", "reason"?}.
    public static let flyyDropResponse = "flyylink.flyydrop.response"

    /// Stops a FlyyDrop request from either side. Body: {"id"}.
    public static let flyyDropCancel = "flyylink.flyydrop.cancel"

    /// A remote control press or typed text for a TV. Body: {"action": "key"|"text", "key"?, "text"?}; see `TvRemote`.
    public static let remote = "flyylink.remote"

    /// Asks the phone for its contacts. Body: {}.
    public static let contactsRequest = "flyylink.contacts.request"

    /// The phone's contacts. Body: {"contacts": [{"name", "numbers": [...]}]}; see `PhoneContacts`.
    public static let contacts = "flyylink.contacts"
}

public enum Capabilities {
    public static let mvp: Set<String> = [
        PacketType.ping,
        PacketType.battery,
        PacketType.batteryRequest,
        PacketType.clipboard,
        PacketType.clipboardConnect,
        PacketType.notification,
        PacketType.notificationRequest,
        PacketType.notificationReply,
        PacketType.notificationAction,
        PacketType.shareRequest,
        PacketType.findMyPhone,
        PacketType.mpris,
        PacketType.mprisRequest,
        PacketType.smsMessages,
        PacketType.smsRequest,
        PacketType.smsRequestConversations,
        PacketType.smsRequestConversation,
        PacketType.telephony,
        PacketType.callRequest,
        PacketType.clipboardFile,
        PacketType.locationRequest,
        PacketType.shareResume,
        PacketType.photosRequest,
        PacketType.photos,
        PacketType.photosDownload,
        PacketType.photosFile,
        PacketType.screenRequest,
        PacketType.screen,
        PacketType.screenInput,
        PacketType.location,
        PacketType.callAudioRequest,
        PacketType.callAudio,
        PacketType.heartbeat,
        PacketType.input,
        PacketType.relay,
        PacketType.flyyDropRequest,
        PacketType.flyyDropResponse,
        PacketType.flyyDropCancel,
        PacketType.remote,
        PacketType.contactsRequest,
        PacketType.contacts,
    ]

    /// What a TV sends and accepts: no texts, calls, contacts, clipboard, battery, location, photos or screen sharing.
    public static let tv: Set<String> = mvp.subtracting([
        PacketType.battery,
        PacketType.batteryRequest,
        PacketType.clipboard,
        PacketType.clipboardConnect,
        PacketType.clipboardFile,
        PacketType.findMyPhone,
        PacketType.smsMessages,
        PacketType.smsRequest,
        PacketType.smsRequestConversations,
        PacketType.smsRequestConversation,
        PacketType.telephony,
        PacketType.callRequest,
        PacketType.callAudioRequest,
        PacketType.callAudio,
        PacketType.locationRequest,
        PacketType.location,
        PacketType.photosRequest,
        PacketType.photos,
        PacketType.photosDownload,
        PacketType.photosFile,
        PacketType.screenRequest,
        PacketType.screen,
        PacketType.screenInput,
        PacketType.input,
        PacketType.relay,
        PacketType.contactsRequest,
        PacketType.contacts,
    ])

    public static func forType(_ type: DeviceType) -> Set<String> { type == .tv ? tv : mvp }
}
