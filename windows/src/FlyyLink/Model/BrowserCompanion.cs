using FlyyLink.Core;
using FlyyLink.Plugins;
using FlyyLink.Shell;
using Microsoft.UI.Dispatching;

namespace FlyyLink.Model;

/// The app's side of the browser add-on: tells browsers which devices and TVs there are, carries out what they
/// ask, hands them sign-in codes from the phone, and lists what their tabs play as players paired devices can control.
/// Mirrors BrowserCompanion.swift.
internal sealed class BrowserCompanion : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly BrowserBridge bridge = new();
    private readonly DeviceRegistry devices;
    private readonly SharePlugin share;
    private readonly DispatcherQueue ui;
    private readonly string downloads = Path.Combine(AppPaths.Cache, "From browser");
    private string? lastState;

    public BrowserCompanion(DeviceRegistry devices, SharePlugin share, DispatcherQueue ui)
    {
        this.devices = devices;
        this.share = share;
        this.ui = ui;

        bridge.OnConnected = connection => ui.TryEnqueue(() => Connected(connection));
        bridge.OnRequest = (connection, request) => ui.TryEnqueue(() => Handle(connection, request));
        bridge.OnClosed = connection =>
        {
            Media.Forget(connection.Key);
            MediaChanged?.Invoke();
        };
    }

    /// What the browsers' tabs play, for `MediaHostPlugin`.
    public BrowserMedia Media { get; } = new();

    /// Called on any thread when a browser's players change.
    public Action? MediaChanged { get; set; }

    /// Builds what the add-on shows. Called on the UI thread.
    public Func<BrowserState>? State { get; set; }

    /// A paired phone by id, or the shown one when the id is null.
    public Func<string?, Device?>? Phone { get; set; }

    /// Places a call from a phone, or the shown one when the id is null.
    public Action<string, string?>? Dial { get; set; }

    /// Plays a web address on a TV found on the network. Returns false when that TV is gone.
    public Func<string, BrowserRequest.PlayOnTv, bool>? PlayOnTv { get; set; }

    public void Start()
    {
        BrowserHosts.Register();
        bridge.Start();
        ClearOldDownloads();
    }

    /// Sends the latest state to every browser when it changed. Call on the UI thread.
    public void Refresh()
    {
        if (State?.Invoke() is not BrowserState state) return;

        var line = BrowserEvents.State(state);
        if (line == lastState) return;

        lastState = line;
        bridge.Broadcast(line);
    }

    /// Passes on the code in a phone notification that just arrived.
    public void Notified(PhoneNotification note, string deviceName)
    {
        if (note.Silent || OneTimeCode.Find(note.Title, note.Text) is not string code) return;

        bridge.Broadcast(BrowserEvents.Code(code, note.AppName, deviceName));
    }

    public void CallChanged(bool active) => bridge.Broadcast(BrowserEvents.Call(active));

    /// Presses a button on a browser player that a paired device controls. Returns false when the name isn't a browser's.
    public bool Control(MediaRequest request)
    {
        if (request.Player is not string name || Media.Find(name) is not (string browser, string player)) return false;

        bridge.Connections.FirstOrDefault(c => c.Key == browser)?.Send(BrowserEvents.Control(player, request.Action, request.SeekTo));
        return true;
    }

    public void Dispose() => bridge.Dispose();

    private void Connected(BrowserConnection connection)
    {
        if (State?.Invoke() is BrowserState state) connection.Send(BrowserEvents.State(state));
    }

    private void Handle(BrowserConnection connection, BrowserRequest request)
    {
        switch (request)
        {
            case BrowserRequest.Hello hello:
                connection.Name = hello.BrowserName;
                break;
            case BrowserRequest.Media media:
                Media.Update(connection.Key, connection.Name, media.Players);
                MediaChanged?.Invoke();
                break;
            case BrowserRequest.Share send:
                Share(connection, send);
                break;
            case BrowserRequest.Call call:
                Dial?.Invoke(call.Number, call.Device);
                Answer(connection, request, Phone?.Invoke(call.Device) is not null, "offline");
                break;
            case BrowserRequest.Text text:
                Answer(connection, request, Phone?.Invoke(text.Device)?.Send(Sms.Send([text.Number], text.Body)) == true, "offline");
                break;
            case BrowserRequest.PlayOnTv play:
                Answer(connection, request, PlayOnTv?.Invoke(play.Tv, play) == true, "tvGone");
                break;
        }
    }

    private void Share(BrowserConnection connection, BrowserRequest.Share request)
    {
        if (devices[request.Device] is not { IsPaired: true } device)
        {
            Answer(connection, request, false, "offline");
            return;
        }

        switch (request.Item)
        {
            case BrowserShare.Link link:
                Answer(connection, request, device.Send(Core.Share.LinkPacket(link.Url)), "offline");
                break;
            case BrowserShare.Text text:
                Answer(connection, request, device.Send(Core.Share.TextPacket(text.Value)), "offline");
                break;
            case BrowserShare.File file when File.Exists(file.Path):
                Task.Run(() => share.Send(file.Path, device));
                Answer(connection, request, true);
                break;
            case BrowserShare.File:
                Answer(connection, request, false, "missing");
                break;
            case BrowserShare.Fetch fetch:
                _ = FetchAsync(connection, request, fetch, device);
                break;
        }
    }

    /// Downloads a picture, video or file from a page, then sends it like any other file.
    private async Task FetchAsync(BrowserConnection connection, BrowserRequest request, BrowserShare.Fetch fetch, Device device)
    {
        try
        {
            using var response = await Http.GetAsync(fetch.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var name = fetch.Name ?? response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? Path.GetFileName(new Uri(fetch.Url).LocalPath);
            var folder = Directory.CreateDirectory(Path.Combine(downloads, Guid.NewGuid().ToString("N")[..8])).FullName;
            var path = Path.Combine(folder, string.IsNullOrWhiteSpace(Path.GetFileName(name)) ? "download" : Path.GetFileName(name));

            await using (var file = File.Create(path))
            {
                await response.Content.CopyToAsync(file);
            }

            share.Send(path, device);
            Answer(connection, request, true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or UriFormatException)
        {
            Answer(connection, request, false, "download");
        }
    }

    private static void Answer(BrowserConnection connection, BrowserRequest request, bool ok, string? error = null)
    {
        if (request.Id is string id) connection.Send(BrowserEvents.Result(id, ok, ok ? null : error));
    }

    /// Files fetched for a device are kept a day, long enough for any transfer to finish.
    private void ClearOldDownloads()
    {
        Task.Run(() =>
        {
            if (!Directory.Exists(downloads)) return;

            foreach (var folder in Directory.EnumerateDirectories(downloads))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-1)) Directory.Delete(folder, recursive: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        });
    }
}
