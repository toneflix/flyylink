using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// How the browser add-on reaches FlyyLink. The browser starts FlyyLink's host, a small helper next to the app, and
/// talks to it over native messaging. The host passes every message on to the app over a local port, as one line of
/// JSON each, after a first line carrying the endpoint's token. Mirrors Browser.swift.
public static class Browser
{
    /// The native messaging host's name, which the add-on connects to.
    public const string HostName = "com.toneflix.flyylink";

    public const string FirefoxId = "browser@flyylink.toneflix.net";

    /// Chromium add-on ids allowed to start the host: the one the unpacked add-on gets from its key, then each store's.
    public static readonly IReadOnlyList<string> ChromiumIds = ["dnlkpekmfbjelcenommpiibkcdbooacj"];

    /// Chrome drops the host when it sends a bigger message than this.
    public const int MaxMessage = 1024 * 1024;

    /// The host manifest that tells a browser where the host is and which add-ons may start it.
    public static string HostManifest(string hostPath, bool firefox)
    {
        var manifest = new JsonObject
        {
            ["name"] = HostName,
            ["description"] = "FlyyLink",
            ["path"] = hostPath,
            ["type"] = "stdio",
        };
        if (firefox)
        {
            manifest["allowed_extensions"] = new JsonArray(FirefoxId);
        }
        else
        {
            manifest["allowed_origins"] = new JsonArray(ChromiumIds.Select(id => (JsonNode)$"chrome-extension://{id}/").ToArray());
        }

        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// A number from a tel: or sms: link or a page, with only what a phone dials. Null when it can't be a number.
    public static string? CleanNumber(string text)
    {
        var trimmed = text.Trim();
        foreach (var scheme in new[] { "tel:", "sms:", "callto:" })
        {
            if (trimmed.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) trimmed = Uri.UnescapeDataString(trimmed[scheme.Length..]);
        }
        trimmed = trimmed.Split('?', ';', ',')[0];

        var number = new StringBuilder();
        foreach (var c in trimmed)
        {
            if (char.IsAsciiDigit(c) || c is '*' or '#' || (c == '+' && number.Length == 0)) number.Append(c);
        }
        var digits = number.ToString().Count(char.IsAsciiDigit);

        return digits is >= 3 and <= 20 ? number.ToString() : null;
    }
}

/// Native messaging's framing: each message is UTF-8 JSON after its length, 4 bytes in the machine's byte order,
/// which is little-endian on every computer FlyyLink runs on.
public static class NativeMessaging
{
    public static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)body.Length);
        body.CopyTo(frame, 4);

        return frame;
    }
}

/// Collects bytes from the browser and hands back whole messages.
public sealed class NativeMessageReader
{
    /// A browser never sends more than this in one message, so a bigger length means the stream is broken.
    public const int MaxLength = 64 * 1024 * 1024;

    private readonly MemoryStream buffer = new();

    /// Set once a length made no sense; nothing more is read.
    public bool IsBroken { get; private set; }

    public void Append(ReadOnlySpan<byte> bytes) => buffer.Write(bytes);

    /// The next whole message, or null until more bytes arrive.
    public string? Next()
    {
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        if (IsBroken || bytes.Length < 4) return null;

        var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (length > MaxLength)
        {
            IsBroken = true;
            return null;
        }
        if (bytes.Length < 4 + length) return null;

        var message = Encoding.UTF8.GetString(bytes.Slice(4, (int)length));
        var rest = bytes[(4 + (int)length)..].ToArray();
        buffer.SetLength(0);
        buffer.Write(rest);

        return message;
    }
}

/// Where the app listens for the host, and the secret the host's first line must carry. Saved where only the
/// user can read it.
public sealed record BrowserEndpoint(int Port, string Token)
{
    public static BrowserEndpoint? Parse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root?["port"].AsLong() is not long port || port is <= 0 or > ushort.MaxValue) return null;
            if (root["token"].AsString() is not { Length: > 0 } token) return null;

            return new BrowserEndpoint((int)port, token);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => new JsonObject { ["port"] = Port, ["token"] = Token }.ToJsonString();

    /// The host's first line.
    public string HelloLine() => new JsonObject { ["token"] = Token }.ToJsonString();

    /// Whether a first line carries this endpoint's token.
    public bool Accepts(string line)
    {
        try
        {
            return JsonNode.Parse(line) is JsonObject root && root["token"].AsString() == Token;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string NewToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
