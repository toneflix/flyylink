using System.Text.Json.Nodes;

namespace FlyyLink.Core;

public enum DeviceType
{
    Desktop,
    Laptop,
    Phone,
    Tablet,
    Tv,
}

public static class DeviceTypes
{
    public static string WireName(this DeviceType type) => type.ToString().ToLowerInvariant();

    /// A Mac or PC rather than a phone, tablet or TV.
    public static bool IsComputer(this DeviceType type) => type is DeviceType.Desktop or DeviceType.Laptop;

    public static bool IsPhone(this DeviceType type) => type is DeviceType.Phone or DeviceType.Tablet;

    /// Phones never pair with each other; they only reach one another through FlyyDrop.
    public static bool PairsWith(this DeviceType type, DeviceType other) => !(type.IsPhone() && other.IsPhone());

    public static DeviceType Parse(string? name) =>
        Enum.TryParse<DeviceType>(name, ignoreCase: true, out var type) && Enum.IsDefined(type) ? type : DeviceType.Desktop;
}

/// Who a device is, as sent in "kdeconnect.identity". Mirrors DeviceIdentity.kt and DeviceIdentity.swift.
public sealed record DeviceIdentity
{
    private static readonly HashSet<char> Forbidden = [.. "\"',;:.!?()[]<>"];

    public required string DeviceId { get; init; }

    public required string DeviceName { get; init; }

    public required DeviceType DeviceType { get; init; }

    public int ProtocolVersion { get; init; } = Protocol.Version;

    public IReadOnlySet<string> IncomingCapabilities { get; init; } = Capabilities.Mvp;

    public IReadOnlySet<string> OutgoingCapabilities { get; init; } = Capabilities.Mvp;

    public int? TcpPort { get; init; }

    /// The phone features the user has on. Devices that don't report them get `PhoneFeatures.All`.
    public PhoneFeatures Features { get; init; } = PhoneFeatures.All;

    public bool Equals(DeviceIdentity? other) =>
        other is not null
        && DeviceId == other.DeviceId
        && DeviceName == other.DeviceName
        && DeviceType == other.DeviceType
        && ProtocolVersion == other.ProtocolVersion
        && IncomingCapabilities.SetEquals(other.IncomingCapabilities)
        && OutgoingCapabilities.SetEquals(other.OutgoingCapabilities)
        && TcpPort == other.TcpPort
        && Features.Equals(other.Features);

    public override int GetHashCode() => HashCode.Combine(DeviceId, DeviceName, DeviceType, TcpPort);

    public static string NewDeviceId() => Guid.NewGuid().ToString("N");

    public static bool IsValidDeviceId(string id) =>
        id.Length is >= 32 and <= 38 && id.All(c => char.IsAscii(c) && (char.IsLetterOrDigit(c) || c == '_'));

    /// Protocol v8 names are 1 to 32 characters with no punctuation from a fixed list.
    public static string SanitizeName(string name, string fallback = "PC")
    {
        var cleaned = new string(name.Where(c => !Forbidden.Contains(c)).ToArray()).Trim();
        var shortened = cleaned.Length > 32 ? cleaned[..32] : cleaned;

        return shortened.Length == 0 ? fallback : shortened;
    }

    /// The identity sent over UDP. Capabilities follow over TLS, and leaving them out keeps the datagram
    /// under one MTU: macOS refuses to send a broadcast that would need fragmenting.
    public NetworkPacket Announcement() =>
        (this with { IncomingCapabilities = new HashSet<string>(), OutgoingCapabilities = new HashSet<string>(), Features = PhoneFeatures.All }).ToPacket();

    public NetworkPacket ToPacket()
    {
        var body = new JsonObject
        {
            ["deviceId"] = DeviceId,
            ["deviceName"] = DeviceName,
            ["deviceType"] = DeviceType.WireName(),
            ["protocolVersion"] = ProtocolVersion,
            ["incomingCapabilities"] = new JsonArray(IncomingCapabilities.Order(StringComparer.Ordinal).Select(c => (JsonNode)c).ToArray()),
            ["outgoingCapabilities"] = new JsonArray(OutgoingCapabilities.Order(StringComparer.Ordinal).Select(c => (JsonNode)c).ToArray()),
        };
        if (TcpPort is int port) body["tcpPort"] = port;
        if (Features.Wire() is { } features) body[PhoneFeatures.BodyKey] = new JsonArray(features.Select(f => (JsonNode)f).ToArray());

        return new NetworkPacket(PacketType.Identity, body);
    }

    public static DeviceIdentity FromPacket(NetworkPacket packet)
    {
        if (packet.Type != PacketType.Identity) throw new ProtocolException($"not an identity packet: {packet.Type}");
        if (packet.GetString("deviceId") is not string id) throw new ProtocolException("missing deviceId");
        if (!IsValidDeviceId(id)) throw new ProtocolException("invalid deviceId");

        var port = packet.GetLong("tcpPort");

        return new DeviceIdentity
        {
            DeviceId = id,
            DeviceName = SanitizeName(packet.GetString("deviceName") ?? "", fallback: "Device"),
            DeviceType = DeviceTypes.Parse(packet.GetString("deviceType")),
            ProtocolVersion = (int)(packet.GetLong("protocolVersion") ?? 0),
            IncomingCapabilities = packet.GetStringList("incomingCapabilities").ToHashSet(),
            OutgoingCapabilities = packet.GetStringList("outgoingCapabilities").ToHashSet(),
            TcpPort = port is > 0 and <= ushort.MaxValue ? (int)port : null,
            Features = PhoneFeatures.FromBody(packet),
        };
    }
}
