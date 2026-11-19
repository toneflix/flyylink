using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// Audio or video playing in a browser tab. Times are in milliseconds. Mirrors BrowserMedia.swift.
public sealed record BrowserPlayer(string Id, string Site)
{
    public string Title { get; init; } = "";

    public string Artist { get; init; } = "";

    public string Album { get; init; } = "";

    /// The page's artwork, an http or https address.
    public string? Artwork { get; init; }

    public bool IsPlaying { get; init; }

    public long Position { get; init; }

    public long Length { get; init; }

    public bool CanSeek { get; init; }

    public bool CanGoNext { get; init; }

    public bool CanGoPrevious { get; init; }

    internal static BrowserPlayer? From(JsonObject root)
    {
        if (root["id"].AsString() is not { Length: > 0 and <= 100 } id) return null;
        if (root["site"].AsString() is not { Length: > 0 } site) return null;

        static long Millis(JsonNode? node) => node.AsDouble() is double value && double.IsFinite(value) && value > 0 ? (long)value : 0;

        return new BrowserPlayer(id, BrowserRequest.Limit(site, 60)!)
        {
            Title = BrowserRequest.Limit(root["title"].AsString(), 300) ?? "",
            Artist = BrowserRequest.Limit(root["artist"].AsString(), 300) ?? "",
            Album = BrowserRequest.Limit(root["album"].AsString(), 300) ?? "",
            Artwork = BrowserRequest.WebUrl(root["artwork"].AsString()),
            IsPlaying = root["playing"].AsBool() ?? false,
            Position = Millis(root["position"]),
            Length = Millis(root["length"]),
            CanSeek = root["canSeek"].AsBool() ?? false,
            CanGoNext = root["canNext"].AsBool() ?? false,
            CanGoPrevious = root["canPrevious"].AsBool() ?? false,
        };
    }
}

/// The players of every connected browser, named for paired devices as "YouTube in Chrome". A name stays with its
/// player while the player lasts, so a phone keeps controlling the right tab.
public sealed class BrowserMedia
{
    private sealed record Entry(string Browser, string BrowserName, BrowserPlayer Player, string Name);

    private readonly object gate = new();
    private List<Entry> entries = [];

    /// Replaces what one browser is playing. `browser` tells connected browsers apart.
    public void Update(string browser, string browserName, IReadOnlyList<BrowserPlayer> players)
    {
        lock (gate)
        {
            var others = entries.Where(e => e.Browser != browser).ToList();
            var previous = entries.Where(e => e.Browser == browser).ToDictionary(e => e.Player.Id, e => e.Name);
            var taken = others.Select(e => e.Name).ToHashSet();

            var current = players.DistinctBy(p => p.Id).ToList();
            var kept = previous.Where(pair => current.Any(p => p.Id == pair.Key)).Select(pair => pair.Value).ToHashSet();

            var updated = new List<Entry>();
            foreach (var player in current)
            {
                var name = previous.TryGetValue(player.Id, out var own) && !taken.Contains(own) ? own : FreeName($"{player.Site} in {browserName}", [.. taken, .. kept]);
                taken.Add(name);
                updated.Add(new Entry(browser, browserName, player, name));
            }

            entries = [.. others, .. updated];
        }
    }

    public void Forget(string browser) => Update(browser, "", []);

    public IReadOnlyList<MediaPlayer> Players
    {
        get
        {
            lock (gate)
            {
                return entries.Select(e => new MediaPlayer(e.Name)
                {
                    Title = e.Player.Title,
                    Artist = e.Player.Artist,
                    Album = e.Player.Album,
                    IsPlaying = e.Player.IsPlaying,
                    Position = e.Player.Position,
                    Length = e.Player.Length,
                    CanSeek = e.Player.CanSeek && e.Player.Length > 0,
                    CanGoNext = e.Player.CanGoNext,
                    CanGoPrevious = e.Player.CanGoPrevious,
                    AlbumArtUrl = e.Player.Artwork,
                }).ToList();
            }
        }
    }

    /// Which browser and which of its players a name stands for.
    public (string Browser, string PlayerId)? Find(string name)
    {
        lock (gate)
        {
            return entries.FirstOrDefault(e => e.Name == name) is Entry entry ? (entry.Browser, entry.Player.Id) : null;
        }
    }

    /// Whether `url` is the artwork of one of the browser's players, so it is safe to download for a device.
    public bool HasArtwork(string url)
    {
        lock (gate) return entries.Any(e => e.Player.Artwork == url);
    }

    private static string FreeName(string wanted, HashSet<string> avoid)
    {
        if (!avoid.Contains(wanted)) return wanted;

        for (var number = 2; ; number++)
        {
            var name = $"{wanted} {number}";
            if (!avoid.Contains(name)) return name;
        }
    }
}
