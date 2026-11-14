using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FlyyLink.Core;

/// Finds TVs on the network that play media sent to them ("Play on TV"). Mirrors DlnaNetwork.kt and DlnaNetwork.swift.
public static class TvFinder
{
    private static readonly string[] Targets = [Ssdp.Renderer, AvTransport.Service];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    /// Searches for the timeout and returns each TV once, sorted by name.
    public static async Task<List<TvRenderer>> SearchAsync(TimeSpan? timeout = null, CancellationToken cancel = default)
    {
        var locations = await CollectLocationsAsync(timeout ?? TimeSpan.FromSeconds(3), cancel);
        var found = await Task.WhenAll(locations.Select(location => DescribeAsync(location, cancel)));

        return found.OfType<TvRenderer>()
            .DistinctBy(tv => tv.Id)
            .OrderBy(tv => tv.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<List<string>> CollectLocationsAsync(TimeSpan timeout, CancellationToken cancel)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var group = new IPEndPoint(IPAddress.Parse(Ssdp.Address), Ssdp.Port);

        // Sent twice, since multicast over Wi-Fi drops packets.
        for (var i = 0; i < 2; i++)
        {
            foreach (var target in Targets)
            {
                var message = Encoding.ASCII.GetBytes(Ssdp.SearchMessage(target));
                try { await socket.SendAsync(message, group, cancel); } catch (SocketException) { }
            }
        }

        var locations = new List<string>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeout);

        while (true)
        {
            UdpReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(deadline.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException)
            {
                break;
            }

            if (Ssdp.Parse(Encoding.UTF8.GetString(result.Buffer)) is SsdpReply reply && !locations.Contains(reply.Location))
                locations.Add(reply.Location);
        }

        return locations;
    }

    private static async Task<TvRenderer?> DescribeAsync(string location, CancellationToken cancel)
    {
        try
        {
            return TvRenderer.Parse(await Http.GetStringAsync(location, cancel), location);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return null;
        }
    }
}

/// Plays media on one TV and controls it.
public sealed class TvPlayer(TvRenderer tv)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public TvRenderer Tv => tv;

    public async Task PlayAsync(string url, string title, string mime, long? size = null)
    {
        await SendAsync(tv.AvTransportUrl, AvTransport.SetUri(url, title, mime, size));
        await SendAsync(tv.AvTransportUrl, AvTransport.Play());
    }

    public Task ResumeAsync() => SendAsync(tv.AvTransportUrl, AvTransport.Play());

    public Task PauseAsync() => SendAsync(tv.AvTransportUrl, AvTransport.Pause());

    public Task StopAsync() => SendAsync(tv.AvTransportUrl, AvTransport.Stop());

    public Task SeekAsync(long positionMs) => SendAsync(tv.AvTransportUrl, AvTransport.Seek(positionMs));

    public async Task<CastPosition?> PositionAsync() => AvTransport.Position(await SendAsync(tv.AvTransportUrl, AvTransport.PositionInfo()));

    public async Task<CastState?> StateAsync() => AvTransport.State(await SendAsync(tv.AvTransportUrl, AvTransport.TransportInfo()));

    public async Task<int?> VolumeAsync() =>
        tv.RenderingControlUrl is string url ? RenderingControl.Volume(await SendAsync(url, RenderingControl.GetVolume())) : null;

    public async Task SetVolumeAsync(int volume)
    {
        if (tv.RenderingControlUrl is string url) await SendAsync(url, RenderingControl.SetVolume(volume));
    }

    private static async Task<string> SendAsync(string url, SoapCall call)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(call.Envelope(), Encoding.UTF8),
        };
        request.Content.Headers.Remove("Content-Type");
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "text/xml; charset=\"utf-8\"");
        request.Headers.TryAddWithoutValidation("SOAPACTION", call.SoapAction);

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new ProtocolException($"{call.Action} failed with HTTP {(int)response.StatusCode}");

        return await response.Content.ReadAsStringAsync();
    }
}

/// A file to serve to a TV.
public interface IMediaSource
{
    string Name { get; }

    string Mime { get; }

    long Size { get; }

    /// Opens the file for reading. Called for every request, since TVs read a file several times.
    Stream Open();
}

/// A file on disk, served by MediaServer.
public sealed record MediaFile(string Path, string Mime) : IMediaSource
{
    public string Name => System.IO.Path.GetFileName(Path);

    public long Size => new FileInfo(Path).Length;

    public Stream Open() => new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
}

/// A small HTTP server that hands files to TVs, with Range support so they can seek.
public sealed class MediaServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, 0);
    private readonly Dictionary<string, IMediaSource> _sources = [];
    private readonly CancellationTokenSource _stop = new();

    public int Port { get; }

    public MediaServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync();
    }

    /// Serves the source and returns its URL as the TV can reach it.
    public string Share(IMediaSource source, string tvHost)
    {
        var token = Guid.NewGuid().ToString();
        lock (_sources) _sources[token] = source;

        var name = Uri.EscapeDataString(source.Name);

        return $"http://{LocalAddressFacing(tvHost)}:{Port}/{token}/{name}";
    }

    /// Stops serving everything shared so far.
    public void Clear()
    {
        lock (_sources) _sources.Clear();
    }

    public void Dispose()
    {
        Clear();
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                using (client)
                {
                    try { await HandleAsync(client.GetStream()); } catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { }
                }
            });
        }
    }

    private async Task HandleAsync(NetworkStream stream)
    {
        stream.ReadTimeout = 15_000;
        if (await ReadHeadAsync(stream) is not string head || HttpRequestHead.Parse(head) is not HttpRequestHead request) return;

        var token = request.Path.TrimStart('/').Split('/')[0];
        IMediaSource? source;
        lock (_sources) source = _sources.GetValueOrDefault(token);

        if (request.Method is not ("GET" or "HEAD")) await RespondAsync(stream, "405 Method Not Allowed");
        else if (source is null) await RespondAsync(stream, "404 Not Found");
        else await ServeAsync(stream, request, source);
    }

    private static async Task ServeAsync(Stream output, HttpRequestHead request, IMediaSource source)
    {
        var size = source.Size;
        var range = ByteRange.Parse(request.Headers.GetValueOrDefault("range"), size);

        if (range is { IsSatisfiable: false })
        {
            await RespondAsync(output, "416 Range Not Satisfiable", ("Content-Range", $"bytes */{size}"));
            return;
        }

        var start = range?.Start ?? 0;
        var length = range?.Length ?? size;
        var headers = new List<(string, string)>
        {
            ("Content-Type", source.Mime),
            ("Content-Length", length.ToString()),
            ("Accept-Ranges", "bytes"),
            ("transferMode.dlna.org", CastKinds.Of(source.Mime) == CastKind.Photo ? "Interactive" : "Streaming"),
            ("contentFeatures.dlna.org", "DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000"),
        };
        if (range is not null) headers.Add(("Content-Range", $"bytes {range.Start}-{range.End}/{size}"));

        await RespondAsync(output, range is not null ? "206 Partial Content" : "200 OK", [.. headers]);
        if (request.Method == "HEAD") return;

        await using var input = source.Open();
        input.Seek(start, SeekOrigin.Begin);

        var buffer = new byte[64 * 1024];
        var left = length;
        while (left > 0)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(left, buffer.Length)));
            if (read <= 0) break;

            await output.WriteAsync(buffer.AsMemory(0, read));
            left -= read;
        }
    }

    private static async Task RespondAsync(Stream output, string status, params (string Name, string Value)[] headers)
    {
        var text = new StringBuilder($"HTTP/1.1 {status}\r\n");
        foreach (var (name, value) in headers) text.Append($"{name}: {value}\r\n");
        if (!headers.Any(h => h.Name == "Content-Length")) text.Append("Content-Length: 0\r\n");
        text.Append("Connection: close\r\n\r\n");

        await output.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()));
    }

    private static async Task<string?> ReadHeadAsync(Stream input)
    {
        var bytes = new List<byte>();
        var one = new byte[1];

        while (bytes.Count < 16 * 1024)
        {
            if (await input.ReadAsync(one) != 1) return null;

            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
                return Encoding.Latin1.GetString(bytes.ToArray());
        }

        return null;
    }

    /// The local address on the network that leads to the host. No packet is sent.
    public static string LocalAddressFacing(string host)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect(host, Ssdp.Port);

        return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
    }
}

/// The TVs seen lately. A TV stays listed until it misses that many searches in a row, since multicast answers get lost.
public sealed class TvList(int misses = 2)
{
    private readonly Dictionary<string, TvRenderer> _known = [];
    private readonly Dictionary<string, int> _missed = [];

    public List<TvRenderer> Tvs
    {
        get { lock (_known) return Sorted(); }
    }

    /// Takes one search's results and returns the TVs to show.
    public List<TvRenderer> Update(IEnumerable<TvRenderer> found)
    {
        lock (_known)
        {
            var list = found.ToList();
            var ids = list.Select(tv => tv.Id).ToHashSet();

            foreach (var id in _known.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                var count = _missed.GetValueOrDefault(id) + 1;
                if (count >= misses)
                {
                    _known.Remove(id);
                    _missed.Remove(id);
                }
                else
                {
                    _missed[id] = count;
                }
            }

            foreach (var tv in list)
            {
                _known[tv.Id] = tv;
                _missed.Remove(tv.Id);
            }

            return Sorted();
        }
    }

    private List<TvRenderer> Sorted() => _known.Values.OrderBy(tv => tv.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
