package com.toneflix.flyylink.protocol

/** KDE Connect protocol version we speak. */
const val PROTOCOL_VERSION = 8

/** UDP port used for discovery broadcasts. */
const val UDP_PORT = 1716

/** TCP ports KDE Connect clients try, in order. */
val TCP_PORT_RANGE = 1716..1764

/** TCP ports a sender opens for payloads such as files. */
val PAYLOAD_PORT_RANGE = 1739..1764

/** Packet types. Standard KDE Connect ones use "kdeconnect.", our extensions use "flyylink.". */
object PacketType {
    const val IDENTITY = "kdeconnect.identity"
    const val PAIR = "kdeconnect.pair"
    const val PING = "kdeconnect.ping"
    const val BATTERY = "kdeconnect.battery"
    const val BATTERY_REQUEST = "kdeconnect.battery.request"
    const val CLIPBOARD = "kdeconnect.clipboard"
    const val CLIPBOARD_CONNECT = "kdeconnect.clipboard.connect"
    const val NOTIFICATION = "kdeconnect.notification"
    const val NOTIFICATION_REQUEST = "kdeconnect.notification.request"
    const val NOTIFICATION_REPLY = "kdeconnect.notification.reply"
    const val NOTIFICATION_ACTION = "kdeconnect.notification.action"
    const val SHARE_REQUEST = "kdeconnect.share.request"
    const val FIND_MY_PHONE = "kdeconnect.findmyphone.request"
    const val MPRIS = "kdeconnect.mpris"
    const val MPRIS_REQUEST = "kdeconnect.mpris.request"
    const val SMS_MESSAGES = "kdeconnect.sms.messages"
    const val SMS_REQUEST = "kdeconnect.sms.request"
    const val SMS_REQUEST_CONVERSATIONS = "kdeconnect.sms.request_conversations"
    const val SMS_REQUEST_CONVERSATION = "kdeconnect.sms.request_conversation"
    const val TELEPHONY = "kdeconnect.telephony"
    const val CONNECTIVITY_REPORT = "kdeconnect.connectivity_report"
    const val CONNECTIVITY_REPORT_REQUEST = "kdeconnect.connectivity_report.request"

    /** Asks the sender to send the rest of a file that stopped. Body: {"transferId", "offset"}. */
    const val SHARE_RESUME = "flyylink.share.resume"

    /** Asks for a page of the phone's photos and videos. Body: {"before"?: ms, "count": n}. */
    const val PHOTOS_REQUEST = "flyylink.photos.request"

    /** A page of photos with small thumbnails. Body: {"photos": [...], "more": bool}. */
    const val PHOTOS = "flyylink.photos"

    /** Asks the phone to send one photo in full: as a normal shared file, or as [PHOTOS_FILE] to view. Body: {"id", "preview"?}. */
    const val PHOTOS_DOWNLOAD = "flyylink.photos.download"

    /** One photo or video in full, for the Mac to show rather than keep. Body: {"id", "name", "mimeType"}, with a payload. */
    const val PHOTOS_FILE = "flyylink.photos.file"

    /** Starts or stops mirroring the phone's screen. Body: {"action": "start"|"stop", "maxSize"?, "bitrate"?}. */
    const val SCREEN_REQUEST = "flyylink.screen.request"

    /** The screen stream, as a payload of size -1. Body: {"width", "height", "codec", "running"}. */
    const val SCREEN = "flyylink.screen"

    /** A tap, swipe, key or text for the mirrored screen. Body: {"action", ...}. */
    const val SCREEN_INPUT = "flyylink.screen.input"

    /** Asks the phone where it is. Body: {}. */
    const val LOCATION_REQUEST = "flyylink.location.request"

    /** Where the phone is. Body: {"latitude", "longitude", "accuracy" (metres), "time" (ms)}. */
    const val LOCATION = "flyylink.location"

    /** A copied picture or file, sent as the payload. Body: {"name": ..., "mimeType": ...}. */
    const val CLIPBOARD_FILE = "flyylink.clipboard.file"

    /** Answer, decline, hang up, mute or unmute a call, or dial a number. Body: {"action": ..., "number"?: ...}. */
    const val CALL_REQUEST = "flyylink.call.request"

    /** Asks the phone about the Bluetooth link for call audio, or to pair with this Mac. Body: {"action": "status"|"pair", "address", "name"?}. */
    const val CALL_AUDIO_REQUEST = "flyylink.callaudio.request"

    /** The phone's side of the call audio link. Body: {"bluetoothOn", "allowed", "paired", "connected", "name"?}. */
    const val CALL_AUDIO = "flyylink.callaudio"

    /** Keeps the link alive and detects a dead one. Body: {"pong": bool}. */
    const val HEARTBEAT = "flyylink.heartbeat"

    /** Keyboard and mouse shared between computers. Body: {"action", ...}; see [RemoteInput]. */
    const val INPUT = "flyylink.input"

    /** A phone's packet passed on through another computer. Body: {"phoneId", "phoneName", "packet"}; see [Relay]. */
    const val RELAY = "flyylink.relay"

    /** Asks to send files, or a link or text, to a device that may not be paired. Body: {"id", "files": [{"name", "size"}], "text"?}; see [FlyyDrop]. */
    const val FLYYDROP_REQUEST = "flyylink.flyydrop.request"

    /** Accepts or declines a FlyyDrop request. Body: {"id", "accept", "reason"?}. */
    const val FLYYDROP_RESPONSE = "flyylink.flyydrop.response"

    /** Stops a FlyyDrop request from either side. Body: {"id"}. */
    const val FLYYDROP_CANCEL = "flyylink.flyydrop.cancel"

    /** A remote control press or typed text for a TV. Body: {"action": "key"|"text", "key"?, "text"?}; see [TvRemote]. */
    const val REMOTE = "flyylink.remote"

    /** Lists, sends, receives, moves or deletes something in the phone's storage. Body: {"id", "action", "path", "to"?}; see [PhoneFiles]. */
    const val FILES_REQUEST = "flyylink.files.request"

    /** The phone's answer to [FILES_REQUEST]. Body: {"id", "ok", "error"?, "files"?, "file"?}, with a payload for downloads. */
    const val FILES = "flyylink.files"

    /** Asks the phone for its contacts. Body: {}. */
    const val CONTACTS_REQUEST = "flyylink.contacts.request"

    /** The phone's contacts. Body: {"contacts": [{"name", "numbers": [...]}]}; see [PhoneContacts]. */
    const val CONTACTS = "flyylink.contacts"

    /** The phone features the user has on, sent when one changes. Body: {"features": [...]}; see [PhoneFeatures]. */
    const val FEATURES = "flyylink.features"
}

/** What each kind of device sends and accepts. Kept in one place so every app advertises the same set. */
object Capabilities {
    val mvp: Set<String> = setOf(
        PacketType.PING,
        PacketType.BATTERY,
        PacketType.BATTERY_REQUEST,
        PacketType.CLIPBOARD,
        PacketType.CLIPBOARD_CONNECT,
        PacketType.NOTIFICATION,
        PacketType.NOTIFICATION_REQUEST,
        PacketType.NOTIFICATION_REPLY,
        PacketType.NOTIFICATION_ACTION,
        PacketType.SHARE_REQUEST,
        PacketType.FIND_MY_PHONE,
        PacketType.MPRIS,
        PacketType.MPRIS_REQUEST,
        PacketType.SMS_MESSAGES,
        PacketType.SMS_REQUEST,
        PacketType.SMS_REQUEST_CONVERSATIONS,
        PacketType.SMS_REQUEST_CONVERSATION,
        PacketType.TELEPHONY,
        PacketType.CONNECTIVITY_REPORT,
        PacketType.CONNECTIVITY_REPORT_REQUEST,
        PacketType.CALL_REQUEST,
        PacketType.CLIPBOARD_FILE,
        PacketType.LOCATION_REQUEST,
        PacketType.SHARE_RESUME,
        PacketType.PHOTOS_REQUEST,
        PacketType.PHOTOS,
        PacketType.PHOTOS_DOWNLOAD,
        PacketType.PHOTOS_FILE,
        PacketType.SCREEN_REQUEST,
        PacketType.SCREEN,
        PacketType.SCREEN_INPUT,
        PacketType.LOCATION,
        PacketType.CALL_AUDIO_REQUEST,
        PacketType.CALL_AUDIO,
        PacketType.HEARTBEAT,
        PacketType.INPUT,
        PacketType.RELAY,
        PacketType.FLYYDROP_REQUEST,
        PacketType.FLYYDROP_RESPONSE,
        PacketType.FLYYDROP_CANCEL,
        PacketType.REMOTE,
        PacketType.FILES_REQUEST,
        PacketType.FILES,
        PacketType.CONTACTS_REQUEST,
        PacketType.CONTACTS,
        PacketType.FEATURES,
    )

    /** What a TV sends and accepts: no texts, calls, contacts, signal, clipboard, battery, location, photos, files or screen sharing. */
    val tv: Set<String> = mvp - setOf(
        PacketType.BATTERY,
        PacketType.BATTERY_REQUEST,
        PacketType.CLIPBOARD,
        PacketType.CLIPBOARD_CONNECT,
        PacketType.CLIPBOARD_FILE,
        PacketType.FIND_MY_PHONE,
        PacketType.SMS_MESSAGES,
        PacketType.SMS_REQUEST,
        PacketType.SMS_REQUEST_CONVERSATIONS,
        PacketType.SMS_REQUEST_CONVERSATION,
        PacketType.TELEPHONY,
        PacketType.CONNECTIVITY_REPORT,
        PacketType.CONNECTIVITY_REPORT_REQUEST,
        PacketType.CALL_REQUEST,
        PacketType.CALL_AUDIO_REQUEST,
        PacketType.CALL_AUDIO,
        PacketType.LOCATION_REQUEST,
        PacketType.LOCATION,
        PacketType.PHOTOS_REQUEST,
        PacketType.PHOTOS,
        PacketType.PHOTOS_DOWNLOAD,
        PacketType.PHOTOS_FILE,
        PacketType.SCREEN_REQUEST,
        PacketType.SCREEN,
        PacketType.SCREEN_INPUT,
        PacketType.INPUT,
        PacketType.RELAY,
        PacketType.FILES_REQUEST,
        PacketType.FILES,
        PacketType.CONTACTS_REQUEST,
        PacketType.CONTACTS,
    )

    fun forType(type: DeviceType): Set<String> = if (type == DeviceType.TV) tv else mvp
}
