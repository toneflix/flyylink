using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// Something the add-on asks of the app, with the `id` its answer carries. Mirrors BrowserMessages.swift.
public abstract record BrowserRequest
{
    public const int MaxText = 10_000;
    public const int MaxUrl = 8_192;

    public string? Id { get; init; }

    /// The add-on says which browser it runs in.
    public sealed record Hello(string BrowserName) : BrowserRequest;

    public sealed record Share(string Device, BrowserShare Item) : BrowserRequest;

    /// Calls a number from the phone. Without a device, the phone the app shows.
    public sealed record Call(string? Device, string Number) : BrowserRequest;

    public sealed record Text(string? Device, string Number, string Body) : BrowserRequest;

    /// Plays a video or song at `Url` on a TV that `TvFinder` found.
    public sealed record PlayOnTv(string Tv, string Url, string Title, string? Mime) : BrowserRequest;

    /// Everything playing in the browser's tabs, which replaces what it said before.
    public sealed record Media(IReadOnlyList<BrowserPlayer> Players) : BrowserRequest;

    public static BrowserRequest? Parse(string line)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (root is null) return null;

        var id = root["id"].AsString();
        BrowserRequest? request = root["type"].AsString() switch
        {
            "hello" when root["browser"].AsString() is { Length: > 0 and <= 100 } name => new Hello(name),
            "share" when root["device"].AsString() is { Length: > 0 } device && BrowserShare.From(root) is BrowserShare item => new Share(device, item),
            "call" when root["number"].AsString() is string number && Browser.CleanNumber(number) is string clean => new Call(root["device"].AsString(), clean),
            "text" when root["number"].AsString() is string number && Browser.CleanNumber(number) is string clean
                && root["body"].AsString() is { Length: > 0 and <= MaxText } body => new Text(root["device"].AsString(), clean, body),
            "playOnTv" when root["tv"].AsString() is { Length: > 0 } tv && WebUrl(root["url"].AsString()) is string url =>
                new PlayOnTv(tv, url, Limit(root["title"].AsString(), 300) ?? "Video", Limit(root["mime"].AsString(), 100)),
            "media" when root["players"] is JsonArray players => new Media(players.OfType<JsonObject>().Select(BrowserPlayer.From).OfType<BrowserPlayer>().Take(20).ToList()),
            _ => null,
        };

        return request is null ? null : request with { Id = Limit(id, 100) };
    }

    /// An http or https address of a sensible length, or null.
    internal static string? WebUrl(string? text) =>
        text is { Length: <= MaxUrl } && Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? text : null;

    internal static string? Limit(string? text, int length) => text is null ? null : text.Length <= length ? text : text[..length];
}

/// What the add-on sends to a device: a page or link to open, text, a file on this computer, or a file to download first.
public abstract record BrowserShare
{
    public sealed record Link(string Url) : BrowserShare;

    public sealed record Text(string Value) : BrowserShare;

    public sealed record File(string Path) : BrowserShare;

    public sealed record Fetch(string Url, string? Name) : BrowserShare;

    internal static BrowserShare? From(JsonObject root)
    {
        if (BrowserRequest.WebUrl(root["link"].AsString()) is string link) return new Link(link);
        if (root["text"].AsString() is { Length: > 0 and <= BrowserRequest.MaxText } text) return new Text(text);
        if (root["file"].AsString() is { Length: > 0 } file && System.IO.Path.IsPathFullyQualified(file)) return new File(file);
        if (BrowserRequest.WebUrl(root["fetch"].AsString()) is string fetch) return new Fetch(fetch, SafeName(root["name"].AsString()));

        return null;
    }

    /// A file name with no folders in it.
    private static string? SafeName(string? name)
    {
        var clean = name is null ? null : new string(name.Where(c => c is not ('/' or '\\' or ':') && !char.IsControl(c)).ToArray()).Trim().TrimStart('.');

        return string.IsNullOrEmpty(clean) ? null : BrowserRequest.Limit(clean, 200);
    }
}

/// A device the add-on can send to.
public sealed record BrowserDevice(string Id, string Name, string Type, bool CanCall, bool CanText);

/// A TV found on the network that plays a video from its address.
public sealed record BrowserTv(string Id, string Name);

/// What the add-on shows: whether the app is ready, and where things can go.
public sealed record BrowserState(string Status, string Computer, string Platform, IReadOnlyList<BrowserDevice> Devices, IReadOnlyList<BrowserTv> Tvs)
{
    public const string Ready = "ready";
    public const string Locked = "locked";

    /// Sent by the host while the app isn't running.
    public const string Closed = "closed";
}

/// What the app tells the add-on, one line of JSON each.
public static class BrowserEvents
{
    public static string State(BrowserState state) => Line(new JsonObject
    {
        ["type"] = "state",
        ["status"] = state.Status,
        ["computer"] = state.Computer,
        ["platform"] = state.Platform,
        ["devices"] = new JsonArray(state.Devices.Select(d => (JsonNode)new JsonObject
        {
            ["id"] = d.Id,
            ["name"] = d.Name,
            ["type"] = d.Type,
            ["canCall"] = d.CanCall,
            ["canText"] = d.CanText,
        }).ToArray()),
        ["tvs"] = new JsonArray(state.Tvs.Select(t => (JsonNode)new JsonObject { ["id"] = t.Id, ["name"] = t.Name }).ToArray()),
    });

    public static string Closed() => Line(new JsonObject { ["type"] = "state", ["status"] = BrowserState.Closed });

    /// A sign-in code that arrived on a phone.
    public static string Code(string code, string app, string device) => Line(new JsonObject
    {
        ["type"] = "code",
        ["code"] = code,
        ["app"] = app,
        ["device"] = device,
    });

    /// A phone call started or ended, so the add-on can pause and resume what plays.
    public static string Call(bool active) => Line(new JsonObject { ["type"] = "call", ["active"] = active });

    /// A paired device pressed a button on one of the browser's players.
    public static string Control(string player, MediaAction? action, long? seekTo)
    {
        var body = new JsonObject { ["type"] = "control", ["player"] = player };
        if (action is MediaAction pressed) body["action"] = pressed.ToString();
        if (seekTo is long position) body["seekTo"] = position;

        return Line(body);
    }

    public static string Result(string id, bool ok, string? error = null)
    {
        var body = new JsonObject { ["type"] = "result", ["id"] = id, ["ok"] = ok };
        if (error is not null) body["error"] = error;

        return Line(body);
    }

    private static string Line(JsonObject body) => body.ToJsonString();
}
