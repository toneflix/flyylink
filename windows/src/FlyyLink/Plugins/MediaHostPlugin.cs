using FlyyLink.Core;
using FlyyLink.Model;
using FlyyLink.Shell;

namespace FlyyLink.Plugins;

/// Lets paired devices see and control what this PC is playing ("kdeconnect.mpris.request"): the Windows media
/// sessions, and the browser tabs the add-on reports.
internal sealed class MediaHostPlugin : IPlugin
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// Bigger artwork than this is not sent to devices.
    private const long MaxArtwork = 4 * 1024 * 1024;

    private readonly DeviceRegistry devices;
    private readonly BrowserCompanion browsers;
    private readonly LocalMedia media = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<MediaPlayer> lastSent = [];

    public MediaHostPlugin(DeviceRegistry devices, BrowserCompanion browsers)
    {
        this.devices = devices;
        this.browsers = browsers;

        media.OnChange = () => _ = BroadcastAsync();
        browsers.MediaChanged = () => _ = BroadcastAsync();
        _ = media.StartAsync();
    }

    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.MprisRequest };

    public void OnPacket(NetworkPacket packet, Device device)
    {
        if (MediaRequest.FromPacket(packet) is MediaRequest request) _ = AnswerAsync(request, device);
    }

    private async Task AnswerAsync(MediaRequest request, Device device)
    {
        if (request.AlbumArtUrl is string art && request.Player is string name)
        {
            await SendArtworkAsync(name, art, device);
            return;
        }

        var changes = request.Action is not null || request.SeekTo is not null;
        if (changes && !browsers.Control(request)) await media.PerformAsync(request);

        var players = await PlayersAsync();
        if (request.WantsPlayerList) device.Send(Media.PlayerListPacket(players.Select(p => p.Name)));
        if ((request.WantsNowPlaying || changes) && players.FirstOrDefault(p => p.Name == request.Player) is MediaPlayer player) device.Send(player.ToPacket());
    }

    /// Browsers also show their tabs as Windows media sessions; the add-on's own player for a tab replaces that one.
    private async Task<IReadOnlyList<MediaPlayer>> PlayersAsync()
    {
        var tabs = browsers.Media.Players;
        var titles = tabs.Select(p => p.Title).Where(t => t.Length > 0).ToHashSet();
        var local = (await media.PlayersAsync()).Where(p => !titles.Contains(p.Title));

        return [.. local, .. tabs];
    }

    /// Sends a browser player's artwork, which only this PC can fetch for the device.
    private async Task SendArtworkAsync(string player, string url, Device device)
    {
        if (!browsers.Media.HasArtwork(url)) return;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            if (bytes.Length == 0 || bytes.Length > MaxArtwork) return;

            device.SendWithPayload(Media.AlbumArtPacket(player, url), bytes.Length, new MemoryStream(bytes));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
        }
    }

    /// Tells every paired device about a change, once per change.
    private async Task BroadcastAsync()
    {
        await gate.WaitAsync();
        try
        {
            var players = await PlayersAsync();
            if (players.SequenceEqual(lastSent)) return;

            var namesChanged = !players.Select(p => p.Name).SequenceEqual(lastSent.Select(p => p.Name));
            lastSent = players;

            foreach (var device in devices.Paired.Where(d => d.Link.Identity.IncomingCapabilities.Contains(PacketType.Mpris)))
            {
                if (namesChanged) device.Send(Media.PlayerListPacket(players.Select(p => p.Name)));
                foreach (var player in players) device.Send(player.ToPacket());
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
