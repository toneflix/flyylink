using FlyyLink.Core;
using FlyyLink.Model;
using Microsoft.Windows.AppNotifications.Builder;

namespace FlyyLink.Plugins;

/// Moves files, links and text between this PC and the phone. Files go to Downloads, links open in the
/// browser, and text lands on the clipboard. A file that stops halfway resumes when the phone reconnects,
/// even after FlyyLink restarts.
internal sealed class SharePlugin(string folder, TransferStore store, ToastCenter toasts, ClipboardPlugin clipboard) : IPlugin
{
    private readonly object gate = new();

    /// The Recent entry of each unfinished transfer, so a resume updates the same line.
    private readonly Dictionary<string, Activity> activities = [];

    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.ShareRequest, PacketType.ShareResume };

    /// Called with each new or updated Recent entry, on any thread.
    public Action<Activity>? OnActivity { get; set; }

    /// Lists the transfers left unfinished last time, and deletes the ones that waited too long.
    public void Restore()
    {
        foreach (var partial in store.RemoveExpired()) TryDelete(partial.Path);

        foreach (var (id, file) in store.PartialFiles)
            Report(id, new Activity(ActivityKind.Failed, Path.GetFileName(file.Path), $"Paused · resumes when {file.DeviceName} reconnects"));
        foreach (var (id, file) in store.OfferedFiles)
            Report(id, new Activity(ActivityKind.Failed, Path.GetFileName(file.Path), $"Stopped · resumes when {file.DeviceName} reconnects"));
    }

    /// Gives up on every transfer that isn't moving right now, so it no longer waits to resume or comes back after a restart.
    public void ForgetUnfinished()
    {
        HashSet<string> moving;
        lock (gate) moving = activities.Where(pair => pair.Value.IsInProgress).Select(pair => pair.Key).ToHashSet();

        foreach (var (id, file) in store.PartialFiles.Where(pair => !moving.Contains(pair.Key)).ToList())
        {
            store.SetPartial(id, null);
            TryDelete(file.Path);
        }
        foreach (var id in store.OfferedFiles.Keys.Where(id => !moving.Contains(id)).ToList()) store.SetOffered(id, null);

        lock (gate)
        {
            foreach (var id in activities.Keys.Where(id => !moving.Contains(id)).ToList()) activities.Remove(id);
        }
    }

    public void OnPaired(Device device)
    {
        foreach (var (id, file) in store.PartialFiles.Where(pair => pair.Value.DeviceId == device.Id))
        {
            var have = File.Exists(file.Path) ? new FileInfo(file.Path).Length : 0;
            device.Send(Share.ResumePacket(id, have));
        }
    }

    public void OnPacket(NetworkPacket packet, Device device)
    {
        if (Share.ResumeRequest(packet) is (string transferId, long offset))
        {
            Resume(transferId, offset, device);
            return;
        }

        switch (Share.FromPacket(packet))
        {
            case Share.File file:
                Receive(packet, file, device);
                break;
            case Share.Link link:
                Open(link.Url, device);
                break;
            case Share.Text text:
                Paste(text.Content, device);
                break;
        }
    }

    // Sending

    /// Offers a file to the phone.
    public void Send(string path, Device device)
    {
        var id = Guid.NewGuid().ToString();
        var activity = Report(id, new Activity(ActivityKind.Sending, Path.GetFileName(path), $"Sending to {device.Name}"));

        store.RemoveExpired();
        store.SetOffered(id, new TransferStore.Offered(path, device.Id, device.Name, DateTimeOffset.Now + TransferStore.Keep));

        Offer(path, id, 0, device, activity);
    }

    /// Sends a file the other device accepted through FlyyDrop. It can't resume, so nothing is kept for later.
    public void Send(string path, Device device, string flyyDropId, Action<long> onBytes, Action<Exception?> onDone)
    {
        var id = Guid.NewGuid().ToString();
        var activity = Report(id, new Activity(ActivityKind.Sending, Path.GetFileName(path), $"FlyyDropping to {device.Name}"));

        Offer(path, id, 0, device, activity, flyyDropId, onBytes, onDone);
    }

    private void Resume(string id, long offset, Device device)
    {
        if (!store.OfferedFiles.TryGetValue(id, out var file) || file.DeviceId != device.Id) return;

        Activity? previous;
        lock (gate) previous = activities.GetValueOrDefault(id);

        var activity = (previous ?? new Activity(ActivityKind.Sending, Path.GetFileName(file.Path), "")) with
        {
            Kind = ActivityKind.Sending,
            Detail = $"Resuming to {device.Name}",
        };
        Report(id, activity);

        Task.Run(() => Offer(file.Path, id, offset, device, activity));
    }

    private void Offer(string path, string id, long offset, Device device, Activity activity,
        string? flyyDropId = null, Action<long>? onBytes = null, Action<Exception?>? onDone = null)
    {
        void Finish(Exception? error)
        {
            if (error is null && flyyDropId is null) store.SetOffered(id, null);

            Report(id, activity with
            {
                Date = DateTimeOffset.Now,
                Progress = null,
                Kind = error is null ? ActivityKind.Sent : ActivityKind.Failed,
                Detail = error is null ? $"Sent to {device.Name}"
                    : flyyDropId is null ? $"Stopped · resumes when {device.Name} reconnects"
                    : $"Transfer to {device.Name} stopped",
            }, finished: error is null || flyyDropId is not null);
            onDone?.Invoke(error);
        }

        FileStream source;
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
        }
        catch (Exception e)
        {
            Finish(e);
            return;
        }

        var size = info.Length;
        var start = Math.Clamp(offset, 0, size);
        source.Seek(start, SeekOrigin.Begin);

        var modified = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        var packet = flyyDropId is null
            ? Share.FilePacket(info.Name, size, modified, id, start)
            : FlyyDrop.File(flyyDropId, new FlyyDrop.Item(info.Name, size));
        var progress = new TransferProgress(size, start);

        void Progress(long bytes)
        {
            onBytes?.Invoke(bytes);
            if (progress.Advance(bytes) is { } reading) Report(id, activity with { Progress = reading });
        }

        device.SendWithPayload(packet, size - start, source, Progress, error =>
        {
            source.Dispose();
            Finish(error);
        });
    }

    // Receiving

    private void Receive(NetworkPacket packet, Share.File file, Device device)
    {
        var partial = file.TransferId is string tid && store.PartialFiles.TryGetValue(tid, out var p) && p.DeviceId == device.Id ? p : null;
        var total = partial?.Total ?? file.Offset + file.Size;

        var target = (partial is not null ? OpenForAppend(partial.Path, file.Offset) : null)
            ?? (file.Offset == 0 ? ReserveFile(Share.SafeName(file.Name)) : null);
        if (target is not (string path, FileStream sink))
        {
            OnActivity?.Invoke(new Activity(ActivityKind.Failed, file.Name, "We couldn't save it to Downloads"));
            return;
        }

        var key = file.TransferId ?? Guid.NewGuid().ToString();
        Activity? previous;
        lock (gate) previous = activities.GetValueOrDefault(key);

        var started = Report(key, (previous ?? new Activity(ActivityKind.Receiving, Path.GetFileName(path), "")) with
        {
            Kind = ActivityKind.Receiving,
            Detail = $"{(partial is null ? "Receiving" : "Resuming")} from {device.Name}",
        });

        // Recorded before any bytes arrive, so a quit or crash midway can still resume.
        if (file.TransferId is string transferId)
            store.SetPartial(transferId, new TransferStore.Partial(path, device.Id, device.Name, total, DateTimeOffset.Now + TransferStore.Keep));

        var progress = new TransferProgress(total, file.Offset);

        _ = Task.Run(async () =>
        {
            Exception? error = null;
            try
            {
                await device.ReceivePayloadAsync(packet, sink, bytes =>
                {
                    if (progress.Advance(bytes) is { } reading) Report(key, started with { Progress = reading });
                });
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                await sink.DisposeAsync();
            }

            Finished(error, key, started, path, total, file.TransferId, device);
        });
    }

    private void Finished(Exception? error, string key, Activity started, string path, long total, string? transferId, Device device)
    {
        var done = started with { Date = DateTimeOffset.Now, Progress = null };

        if (error is null)
        {
            done = done with { Kind = ActivityKind.Received, Detail = $"Received · {Bytes.Text(total)}", Path = path };
            if (transferId is not null) store.SetPartial(transferId, null);
            AnnounceFile(path, device.Name);
        }
        else if (transferId is not null)
        {
            done = done with { Kind = ActivityKind.Failed, Detail = $"Paused · resumes when {device.Name} reconnects" };
        }
        else
        {
            TryDelete(path);
            done = done with { Kind = ActivityKind.Failed, Detail = $"Transfer from {device.Name} stopped" };
        }

        Report(key, done, finished: error is null || transferId is null);
    }

    /// Tells the user a file arrived. Clicking it opens the file, and its second button shows it in Explorer.
    private void AnnounceFile(string path, string deviceName)
    {
        toasts.Show(key => new AppNotificationBuilder()
            .AddText(Path.GetFileName(path))
            .AddText($"From {deviceName} · Saved to Downloads")
            .AddButton(ToastCenter.Button("Open", "open", key))
            .AddButton(ToastCenter.Button("Show in folder", "folder", key)),
            (action, _) =>
            {
                if (action == "folder") Shell.Explorer.Reveal(path);
                else Shell.Explorer.OpenFile(path);
            });
    }

    /// Shows `activity` in Recent and remembers it under `id` until the transfer is `finished`.
    private Activity Report(string id, Activity activity, bool finished = false)
    {
        lock (gate)
        {
            if (finished) activities.Remove(id);
            else activities[id] = activity;
        }

        OnActivity?.Invoke(activity);

        return activity;
    }

    /// Opens a partly received file to add the rest, if it is as long as the sender thinks.
    private static (string, FileStream)? OpenForAppend(string path, long offset)
    {
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true);
            if (stream.Length == offset)
            {
                stream.Seek(0, SeekOrigin.End);
                return (path, stream);
            }

            stream.Dispose();
        }
        catch (IOException)
        {
        }

        return null;
    }

    /// Creates an empty file with a name not taken yet, such as "photo (2).jpg".
    private (string, FileStream)? ReserveFile(string name)
    {
        lock (gate)
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            var path = Path.Combine(folder, name);

            for (var copy = 2; File.Exists(path); copy++) path = Path.Combine(folder, $"{stem} ({copy}){extension}");

            try
            {
                return (path, new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true));
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    // Links and text

    private void Open(string link, Device device)
    {
        // Anything but a web link could launch an app, so only offer it as text.
        if (!Uri.TryCreate(link, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            Paste(link, device);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        OnActivity?.Invoke(new Activity(ActivityKind.Link, url.Host, $"Opened from {device.Name}") { Url = url.AbsoluteUri });
    }

    private void Paste(string text, Device device)
    {
        clipboard.WriteText(text);

        var preview = text.Split('\n').FirstOrDefault() ?? text;
        OnActivity?.Invoke(new Activity(ActivityKind.Text, $"“{preview}”", $"Copied from {device.Name}"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
