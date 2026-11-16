using FlyyLink.Core;
using FlyyLink.Model;

namespace FlyyLink.Plugins;

/// What a phone is playing, with when we heard about it so the position can keep moving.
public sealed record NowPlaying(MediaPlayer Player, DateTimeOffset ReceivedAt, string? Artwork)
{
    /// Where playback should be now, in milliseconds.
    public long Position(DateTimeOffset now)
    {
        if (!Player.IsPlaying) return Player.Position;

        var moved = (long)(now - ReceivedAt).TotalMilliseconds;

        return Player.Length > 0 ? Math.Min(Player.Position + moved, Player.Length) : Player.Position + moved;
    }
}

/// Shows and controls the phone's media players ("kdeconnect.mpris").
internal sealed class MediaPlugin : IPlugin
{
    private const string OwnArtPrefix = "flyylink://art/";

    private readonly Dictionary<string, Dictionary<string, NowPlaying>> players = [];
    private readonly object gate = new();
    private readonly string artFolder = Directory.CreateDirectory(Path.Combine(AppPaths.Cache, "AlbumArt")).FullName;

    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.Mpris };

    /// Called on a network thread when something changed.
    public Action? OnChange { get; set; }

    public void OnPaired(Device device) => device.Send(MediaRequest.PlayerList().ToPacket());

    public void OnFeaturesChanged(Device device) => OnPaired(device);

    public void OnPacket(NetworkPacket packet, Device device)
    {
        if (Media.PlayerList(packet) is { } names) UpdateList(names, device);
        else if (Media.AlbumArtUrl(packet) is string url) _ = ReceiveArtAsync(packet, url, device);
        else if (MediaPlayer.FromPacket(packet) is MediaPlayer player) Update(player, device);
    }

    /// The player to show: one that is playing, otherwise the last one with a song.
    public NowPlaying? NowPlaying(string deviceId)
    {
        List<NowPlaying> candidates;
        lock (gate) candidates = players.GetValueOrDefault(deviceId)?.Values.Where(p => p.Player.Title.Length > 0).ToList() ?? [];

        return candidates.FirstOrDefault(p => p.Player.IsPlaying) ?? candidates.MaxBy(p => p.ReceivedAt);
    }

    public void Forget(string deviceId)
    {
        lock (gate) players.Remove(deviceId);
    }

    private Dictionary<string, NowPlaying> Of(string deviceId)
    {
        if (!players.TryGetValue(deviceId, out var list)) players[deviceId] = list = [];

        return list;
    }

    private void UpdateList(IReadOnlyList<string> names, Device device)
    {
        List<string> fresh;
        lock (gate)
        {
            var list = Of(device.Id);
            foreach (var gone in list.Keys.Where(name => !names.Contains(name)).ToList()) list.Remove(gone);
            fresh = names.Where(name => !list.ContainsKey(name)).ToList();
        }

        fresh.ForEach(name => device.Send(MediaRequest.NowPlaying(name).ToPacket()));
        OnChange?.Invoke();
    }

    private void Update(MediaPlayer player, Device device)
    {
        var artwork = player.AlbumArtUrl is string url ? ArtFile(url) : null;
        var cached = artwork is not null && File.Exists(artwork);

        lock (gate) Of(device.Id)[player.Name] = new NowPlaying(player, DateTimeOffset.Now, cached ? artwork : null);

        if (player.AlbumArtUrl is string art && artwork is not null && !cached) device.Send(MediaRequest.AlbumArt(player.Name, art).ToPacket());

        OnChange?.Invoke();
    }

    private async Task ReceiveArtAsync(NetworkPacket packet, string url, Device device)
    {
        var target = ArtFile(url);
        var partial = $"{target}.{Guid.NewGuid():N}.part";

        try
        {
            await using (var sink = File.Create(partial))
            {
                await device.ReceivePayloadAsync(packet, sink);
            }

            File.Move(partial, target, overwrite: true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            File.Delete(partial);
            return;
        }

        lock (gate)
        {
            var list = Of(device.Id);
            foreach (var (name, entry) in list.Where(pair => pair.Value.Player.AlbumArtUrl == url).ToList())
                list[name] = entry with { Artwork = target };
        }

        OnChange?.Invoke();
    }

    /// A safe local file name for an art url: our own urls carry a hash, others get hashed.
    private string ArtFile(string url)
    {
        var suffix = url.StartsWith(OwnArtPrefix, StringComparison.Ordinal) ? url[OwnArtPrefix.Length..] : "";
        var name = suffix.Length == 32 && suffix.All(Uri.IsHexDigit) ? suffix : PhoneNotification.HashIcon(System.Text.Encoding.UTF8.GetBytes(url));

        return Path.Combine(artFolder, $"{name}.jpg");
    }
}
