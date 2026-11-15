using System.Collections.Concurrent;
using FlyyLink.Core;

namespace FlyyLink.Plugins;

/// A request about the phone's storage that failed. `Failure` is null when the phone isn't connected or stopped answering.
internal sealed class PhoneFilesException(PhoneFiles.Failure? failure) : Exception(failure?.Wire() ?? "offline")
{
    public PhoneFiles.Failure? Failure { get; } = failure;
}

/// Asks a phone about its storage ("flyylink.files.request") and matches each answer ("flyylink.files") to its request.
internal sealed class FilesPlugin : IPlugin
{
    /// How long the phone may take to answer, counted from the request or from the end of an upload.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<long, Waiting> waiting = new();
    private long lastId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private sealed record Waiting(string DeviceId, string? SaveTo, TaskCompletionSource<PhoneFiles.Answer> Answer);

    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.Files };

    /// Sends one request. `upload` is the file an upload sends; `saveTo` is where a download goes.
    public async Task<PhoneFiles.Answer> SendAsync(
        Device device,
        PhoneFiles.Action action,
        string path,
        string? to = null,
        string? upload = null,
        string? saveTo = null,
        CancellationToken cancellation = default)
    {
        var id = Interlocked.Increment(ref lastId);
        var answer = new TaskCompletionSource<PhoneFiles.Answer>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting[id] = new Waiting(device.Id, saveTo, answer);

        try
        {
            var packet = PhoneFiles.RequestPacket(new PhoneFiles.Request(id, action, path, to));
            if (upload is null)
            {
                if (!device.Send(packet)) throw new PhoneFilesException(null);
                Expire(id);
            }
            else
            {
                await SendUploadAsync(device, packet, upload, id);
            }

            using (cancellation.Register(() => answer.TrySetCanceled(cancellation)))
            {
                return await answer.Task;
            }
        }
        finally
        {
            waiting.TryRemove(id, out _);
        }
    }

    public void OnPacket(NetworkPacket packet, Device device)
    {
        if (PhoneFiles.AnswerOf(packet) is not PhoneFiles.Answer answer) return;
        if (!waiting.TryGetValue(answer.Id, out var request) || request.DeviceId != device.Id) return;

        // Answered, so it no longer expires, even while a download is still arriving.
        waiting.TryRemove(answer.Id, out _);

        if (answer.Failure is PhoneFiles.Failure failure) request.Answer.TrySetException(new PhoneFilesException(failure));
        else if (request.SaveTo is string target && packet.PayloadSize is not null) _ = ReceiveAsync(packet, device, target, answer, request.Answer);
        else request.Answer.TrySetResult(answer);
    }

    private async Task SendUploadAsync(Device device, NetworkPacket packet, string upload, long id)
    {
        var sent = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = File.OpenRead(upload);

        device.SendWithPayload(packet, source.Length, source, completion: error =>
        {
            source.Dispose();
            sent.TrySetResult(error);
        });

        if (await sent.Task is not null) throw new PhoneFilesException(null);

        Expire(id);
    }

    private static async Task ReceiveAsync(NetworkPacket packet, Device device, string target, PhoneFiles.Answer answer, TaskCompletionSource<PhoneFiles.Answer> result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (var sink = File.Create(target))
            {
                await device.ReceivePayloadAsync(packet, sink);
            }

            result.TrySetResult(answer);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            File.Delete(target);
            result.TrySetException(new PhoneFilesException(null));
        }
    }

    private void Expire(long id) => _ = Task.Delay(Patience).ContinueWith(_ =>
    {
        if (waiting.TryGetValue(id, out var request)) request.Answer.TrySetException(new PhoneFilesException(null));
    }, TaskScheduler.Default);
}
