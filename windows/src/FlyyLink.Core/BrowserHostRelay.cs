using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// The host's work: passes the browser's framed messages to the app's local port as lines, and the app's lines back
/// as frames. While the app isn't running it tells the browser so, and keeps trying; `launch` opens it. The last `hello` and `media`
/// are sent again on every connection, so an app that starts later still knows the browser. Mirrors BrowserHostRelay.swift.
public sealed class BrowserHostRelay(Stream browserIn, Stream browserOut, Func<BrowserEndpoint?> endpoint)
{
    private static readonly string[] Replayed = ["hello", "media"];

    private readonly SemaphoreSlim writing = new(1, 1);
    private readonly Dictionary<string, string> latest = [];
    private readonly object gate = new();
    private NetworkStream? app;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// Opens FlyyLink when the add-on asks with `launch`, such as from its "Open FlyyLink" button.
    public Action? Launch { get; init; }

    /// Runs until the browser closes the host's input.
    public async Task RunAsync()
    {
        using var stop = new CancellationTokenSource();
        var appLoop = KeepConnectedAsync(stop.Token);

        await ReadBrowserAsync();

        stop.Cancel();
        lock (gate) app?.Close();
        try
        {
            await appLoop;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReadBrowserAsync()
    {
        var reader = new NativeMessageReader();
        var chunk = new byte[64 * 1024];

        while (true)
        {
            var read = await browserIn.ReadAsync(chunk);
            if (read == 0) return;

            reader.Append(chunk.AsSpan(0, read));
            while (reader.Next() is string message) await ForwardAsync(message);
            if (reader.IsBroken) return;
        }
    }

    private async Task ForwardAsync(string message)
    {
        string line;
        try
        {
            var root = JsonNode.Parse(message) as JsonObject;
            if (root is null) return;

            line = root.ToJsonString();
            var type = root["type"].AsString();
            if (type == "launch")
            {
                Launch?.Invoke();
                return;
            }
            if (type is not null && Replayed.Contains(type))
            {
                lock (gate) latest[type] = line;
            }
        }
        catch (JsonException)
        {
            return;
        }

        NetworkStream? stream;
        lock (gate) stream = app;
        if (stream is null) return;

        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
    }

    private async Task KeepConnectedAsync(CancellationToken cancellation)
    {
        var toldClosed = false;

        while (!cancellation.IsCancellationRequested)
        {
            if (await ConnectAsync(cancellation) is TcpClient client)
            {
                toldClosed = false;
                await RelayAppAsync(client, cancellation);
            }

            if (!toldClosed)
            {
                await WriteBrowserAsync(BrowserEvents.Closed());
                toldClosed = true;
            }

            await Task.Delay(RetryDelay, cancellation);
        }
    }

    private async Task<TcpClient?> ConnectAsync(CancellationToken cancellation)
    {
        if (endpoint() is not BrowserEndpoint target) return null;

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, target.Port, cancellation);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(target.HelloLine() + "\n"), cancellation);

            string[] replay;
            lock (gate) replay = Replayed.Where(latest.ContainsKey).Select(type => latest[type]).ToArray();
            foreach (var line in replay) await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellation);

            lock (gate) app = stream;
            return client;
        }
        catch (SocketException)
        {
            client.Dispose();
            return null;
        }
    }

    private async Task RelayAppAsync(TcpClient client, CancellationToken cancellation)
    {
        using (client)
        {
            var lines = new LineReader(client.GetStream());
            try
            {
                while (await lines.ReadLineAsync(cancellation) is byte[] line)
                {
                    if (line.Length <= Browser.MaxMessage) await WriteBrowserAsync(Encoding.UTF8.GetString(line));
                }
            }
            catch (Exception e) when (e is IOException or ProtocolException or ObjectDisposedException)
            {
            }
            finally
            {
                lock (gate) app = null;
            }
        }
    }

    private async Task WriteBrowserAsync(string json)
    {
        await writing.WaitAsync();
        try
        {
            await browserOut.WriteAsync(NativeMessaging.Frame(json));
            await browserOut.FlushAsync();
        }
        finally
        {
            writing.Release();
        }
    }
}
