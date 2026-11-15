using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// A phone feature the user turns on in the app. Mirrors Features.kt and Features.swift.
public enum PhoneFeature
{
    Notifications,
    Media,
    Messages,
    Clipboard,
    Photos,
    Files,
    Mirroring,
    Control,
    Location,
}

/// The packets each feature covers: `Sends` from the phone, `Accepts` from computers, so neither goes through while it is off.
public static class PhoneFeatureInfo
{
    public static string WireName(this PhoneFeature feature) => feature.ToString().ToLowerInvariant();

    public static PhoneFeature? FromWire(string name) =>
        Enum.GetValues<PhoneFeature>().Cast<PhoneFeature?>().FirstOrDefault(f => f!.Value.WireName() == name);

    public static IReadOnlySet<string> Sends(this PhoneFeature feature) => feature switch
    {
        PhoneFeature.Notifications => new HashSet<string> { PacketType.Notification },
        PhoneFeature.Media => new HashSet<string> { PacketType.Mpris },
        PhoneFeature.Messages => new HashSet<string> { PacketType.SmsMessages, PacketType.Telephony, PacketType.Contacts, PacketType.CallAudio },
        PhoneFeature.Clipboard => new HashSet<string> { PacketType.Clipboard, PacketType.ClipboardConnect, PacketType.ClipboardFile },
        PhoneFeature.Photos => new HashSet<string> { PacketType.Photos, PacketType.PhotosFile },
        PhoneFeature.Files => new HashSet<string> { PacketType.Files },
        PhoneFeature.Mirroring => new HashSet<string> { PacketType.Screen },
        PhoneFeature.Location => new HashSet<string> { PacketType.Location },
        _ => new HashSet<string>(),
    };

    public static IReadOnlySet<string> Accepts(this PhoneFeature feature) => feature switch
    {
        PhoneFeature.Notifications => new HashSet<string> { PacketType.NotificationRequest, PacketType.NotificationReply, PacketType.NotificationAction },
        PhoneFeature.Media => new HashSet<string> { PacketType.MprisRequest },
        PhoneFeature.Messages => new HashSet<string>
        {
            PacketType.SmsRequest,
            PacketType.SmsRequestConversations,
            PacketType.SmsRequestConversation,
            PacketType.CallRequest,
            PacketType.ContactsRequest,
            PacketType.CallAudioRequest,
        },
        PhoneFeature.Clipboard => new HashSet<string> { PacketType.Clipboard, PacketType.ClipboardConnect, PacketType.ClipboardFile },
        PhoneFeature.Photos => new HashSet<string> { PacketType.PhotosRequest, PacketType.PhotosDownload },
        PhoneFeature.Files => new HashSet<string> { PacketType.FilesRequest },
        PhoneFeature.Mirroring => new HashSet<string> { PacketType.ScreenRequest },
        PhoneFeature.Control => new HashSet<string> { PacketType.ScreenInput },
        PhoneFeature.Location => new HashSet<string> { PacketType.LocationRequest },
        _ => new HashSet<string>(),
    };
}

/// The features a device has on. `Reported` is null when the device doesn't say, as computers, TVs and
/// older phones don't, and then every feature counts as on.
public sealed record PhoneFeatures(IReadOnlySet<PhoneFeature>? Reported)
{
    public const string BodyKey = "features";

    public static readonly PhoneFeatures All = new((IReadOnlySet<PhoneFeature>?)null);

    public static PhoneFeatures Of(params PhoneFeature[] features) => new(features.ToHashSet());

    public bool Contains(PhoneFeature feature) => Reported?.Contains(feature) ?? true;

    /// False when `type` belongs to a feature that is off on the sending side.
    public bool Sends(string type) => !Enum.GetValues<PhoneFeature>().Any(f => f.Sends().Contains(type) && !Contains(f));

    /// False when `type` belongs to a feature that is off on the receiving side.
    public bool Accepts(string type) => !Enum.GetValues<PhoneFeature>().Any(f => f.Accepts().Contains(type) && !Contains(f));

    /// The wire names of the features that are on, for the identity and `ToPacket()`; null when not reported.
    public IReadOnlyList<string>? Wire() => Reported?.Select(f => f.WireName()).Order(StringComparer.Ordinal).ToList();

    /// Tells paired devices which features are on, after one changes.
    public NetworkPacket ToPacket() =>
        new(PacketType.Features, new JsonObject { [BodyKey] = new JsonArray((Wire() ?? []).Select(n => (JsonNode)n).ToArray()) });

    /// Reads the list in an identity or "flyylink.features" packet. Unknown names are skipped.
    public static PhoneFeatures FromBody(NetworkPacket packet)
    {
        if (!packet.Body.ContainsKey(BodyKey)) return All;

        var features = packet.GetStringList(BodyKey).Select(PhoneFeatureInfo.FromWire).OfType<PhoneFeature>().ToHashSet();

        return new PhoneFeatures(features);
    }

    public static PhoneFeatures? FromPacket(NetworkPacket packet) =>
        packet.Type == PacketType.Features && packet.Body.ContainsKey(BodyKey) ? FromBody(packet) : null;

    public bool Equals(PhoneFeatures? other) =>
        other is not null && (Reported is null ? other.Reported is null : other.Reported is not null && Reported.SetEquals(other.Reported));

    public override int GetHashCode() => Reported?.Count ?? -1;
}
