namespace FlyyLink.Core;

public static class Protocol
{
    /// KDE Connect protocol version we speak.
    public const int Version = 8;

    /// UDP port used for discovery broadcasts.
    public const int UdpPort = 1716;

    /// TCP ports KDE Connect clients try, in order.
    public static readonly PortRange TcpPorts = new(1716, 1764);

    /// TCP ports a sender opens for payloads such as files.
    public static readonly PortRange PayloadPorts = new(1739, 1764);
}

public readonly record struct PortRange(int First, int Last)
{
    public IEnumerable<int> All => Enumerable.Range(First, Last - First + 1);
}

/// Packet types. Standard KDE Connect ones use "kdeconnect.", our extensions use "flyylink.".
/// Keep in sync with PacketType.kt and PacketType.swift.
public static class PacketType
{
    public const string Identity = "kdeconnect.identity";
    public const string Pair = "kdeconnect.pair";
    public const string Ping = "kdeconnect.ping";
    public const string Battery = "kdeconnect.battery";
    public const string BatteryRequest = "kdeconnect.battery.request";
    public const string Clipboard = "kdeconnect.clipboard";
    public const string ClipboardConnect = "kdeconnect.clipboard.connect";
    public const string Notification = "kdeconnect.notification";
    public const string NotificationRequest = "kdeconnect.notification.request";
    public const string NotificationReply = "kdeconnect.notification.reply";
    public const string NotificationAction = "kdeconnect.notification.action";
    public const string ShareRequest = "kdeconnect.share.request";
    public const string FindMyPhone = "kdeconnect.findmyphone.request";
    public const string Mpris = "kdeconnect.mpris";
    public const string MprisRequest = "kdeconnect.mpris.request";
    public const string SmsMessages = "kdeconnect.sms.messages";
    public const string SmsRequest = "kdeconnect.sms.request";
    public const string SmsRequestConversations = "kdeconnect.sms.request_conversations";
    public const string SmsRequestConversation = "kdeconnect.sms.request_conversation";
    public const string Telephony = "kdeconnect.telephony";
    public const string ConnectivityReport = "kdeconnect.connectivity_report";
    public const string ConnectivityReportRequest = "kdeconnect.connectivity_report.request";

    /// Asks the sender to send the rest of a file that stopped. Body: {"transferId", "offset"}.
    public const string ShareResume = "flyylink.share.resume";

    /// Asks for a page of the phone's photos and videos. Body: {"before"?: ms, "count": n}.
    public const string PhotosRequest = "flyylink.photos.request";

    /// A page of photos with small thumbnails. Body: {"photos": [...], "more": bool}.
    public const string Photos = "flyylink.photos";

    /// Asks the phone to send one photo in full. Body: {"id", "preview"?}.
    public const string PhotosDownload = "flyylink.photos.download";

    /// One photo or video in full, to show rather than keep. Body: {"id", "name", "mimeType"}, with a payload.
    public const string PhotosFile = "flyylink.photos.file";

    /// Starts or stops mirroring the phone's screen. Body: {"action": "start"|"stop", "maxSize"?, "bitrate"?}.
    public const string ScreenRequest = "flyylink.screen.request";

    /// The screen stream, as a payload of size -1. Body: {"width", "height", "codec", "running"}.
    public const string Screen = "flyylink.screen";

    /// A tap, swipe, key or text for the mirrored screen. Body: {"action", ...}.
    public const string ScreenInput = "flyylink.screen.input";

    /// Asks the phone where it is. Body: {}.
    public const string LocationRequest = "flyylink.location.request";

    /// Where the phone is. Body: {"latitude", "longitude", "accuracy" (metres), "time" (ms)}.
    public const string Location = "flyylink.location";

    /// A copied picture or file, sent as the payload. Body: {"name", "mimeType"}.
    public const string ClipboardFile = "flyylink.clipboard.file";

    /// Answer, decline, hang up, mute or unmute a call, or dial a number. Body: {"action", "number"?}.
    public const string CallRequest = "flyylink.call.request";

    /// Asks the phone about the Bluetooth link for call audio, or to pair with this computer. Body: {"action": "status"|"pair", "address", "name"?}.
    public const string CallAudioRequest = "flyylink.callaudio.request";

    /// The phone's side of the call audio link. Body: {"bluetoothOn", "allowed", "paired", "connected", "name"?}.
    public const string CallAudio = "flyylink.callaudio";

    /// Keeps the link alive and detects a dead one. Body: {"pong": bool}.
    public const string Heartbeat = "flyylink.heartbeat";

    /// Keyboard and mouse shared between computers. Body: {"action", ...}; see `RemoteInput`.
    public const string Input = "flyylink.input";

    /// A phone's packet passed on through another computer. Body: {"phoneId", "phoneName", "packet"}; see `Relay`.
    public const string Relay = "flyylink.relay";

    /// Asks to send files, or a link or text, to a device that may not be paired. Body: {"id", "files": [{"name", "size"}], "text"?}; see `FlyyDrop`.
    public const string FlyyDropRequest = "flyylink.flyydrop.request";

    /// Accepts or declines a FlyyDrop request. Body: {"id", "accept", "reason"?}.
    public const string FlyyDropResponse = "flyylink.flyydrop.response";

    /// Stops a FlyyDrop request from either side. Body: {"id"}.
    public const string FlyyDropCancel = "flyylink.flyydrop.cancel";

    /// A remote control press or typed text for a TV. Body: {"action": "key"|"text", "key"?, "text"?}; see `TvRemote`.
    public const string Remote = "flyylink.remote";

    /// Lists, sends, receives, moves or deletes something in the phone's storage. Body: {"id", "action", "path", "to"?}; see `PhoneFiles`.
    public const string FilesRequest = "flyylink.files.request";

    /// The phone's answer to "flyylink.files.request". Body: {"id", "ok", "error"?, "files"?, "file"?}, with a payload for downloads.
    public const string Files = "flyylink.files";
}

public static class Capabilities
{
    public static readonly IReadOnlySet<string> Mvp = new HashSet<string>
    {
        PacketType.Ping,
        PacketType.Battery,
        PacketType.BatteryRequest,
        PacketType.Clipboard,
        PacketType.ClipboardConnect,
        PacketType.Notification,
        PacketType.NotificationRequest,
        PacketType.NotificationReply,
        PacketType.NotificationAction,
        PacketType.ShareRequest,
        PacketType.FindMyPhone,
        PacketType.Mpris,
        PacketType.MprisRequest,
        PacketType.SmsMessages,
        PacketType.SmsRequest,
        PacketType.SmsRequestConversations,
        PacketType.SmsRequestConversation,
        PacketType.Telephony,
        PacketType.ConnectivityReport,
        PacketType.ConnectivityReportRequest,
        PacketType.CallRequest,
        PacketType.ClipboardFile,
        PacketType.LocationRequest,
        PacketType.ShareResume,
        PacketType.PhotosRequest,
        PacketType.Photos,
        PacketType.PhotosDownload,
        PacketType.PhotosFile,
        PacketType.ScreenRequest,
        PacketType.Screen,
        PacketType.ScreenInput,
        PacketType.Location,
        PacketType.CallAudioRequest,
        PacketType.CallAudio,
        PacketType.Heartbeat,
        PacketType.Input,
        PacketType.Relay,
        PacketType.FlyyDropRequest,
        PacketType.FlyyDropResponse,
        PacketType.FlyyDropCancel,
        PacketType.Remote,
        PacketType.FilesRequest,
        PacketType.Files,
    };

    /// What a TV sends and accepts: no texts, calls, signal, clipboard, battery, location, photos, files or screen sharing.
    public static readonly IReadOnlySet<string> Tv = Mvp.Except(
    [
        PacketType.Battery,
        PacketType.BatteryRequest,
        PacketType.Clipboard,
        PacketType.ClipboardConnect,
        PacketType.ClipboardFile,
        PacketType.FindMyPhone,
        PacketType.SmsMessages,
        PacketType.SmsRequest,
        PacketType.SmsRequestConversations,
        PacketType.SmsRequestConversation,
        PacketType.Telephony,
        PacketType.ConnectivityReport,
        PacketType.ConnectivityReportRequest,
        PacketType.CallRequest,
        PacketType.CallAudioRequest,
        PacketType.CallAudio,
        PacketType.LocationRequest,
        PacketType.Location,
        PacketType.PhotosRequest,
        PacketType.Photos,
        PacketType.PhotosDownload,
        PacketType.PhotosFile,
        PacketType.ScreenRequest,
        PacketType.Screen,
        PacketType.ScreenInput,
        PacketType.Input,
        PacketType.Relay,
        PacketType.FilesRequest,
        PacketType.Files,
    ]).ToHashSet();

    public static IReadOnlySet<string> ForType(DeviceType type) => type == DeviceType.Tv ? Tv : Mvp;
}
