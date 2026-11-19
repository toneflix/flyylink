using System.Net;
using System.Net.Sockets;
using System.Text;
using FlyyLink.Core;
using FlyyLink.Model;
using Microsoft.Win32;

namespace FlyyLink.Shell;

/// One browser running the FlyyLink add-on, connected through the host.
internal sealed class BrowserConnection(NetworkStream stream)
{
    private readonly SemaphoreSlim writing = new(1, 1);

    /// Tells connected browsers apart in `BrowserMedia`.
    public string Key { get; } = Guid.NewGuid().ToString("N");

    /// The browser's name from its hello, such as "Chrome".
    public string Name { get; set; } = "Browser";

    public async void Send(string line)
    {
        await writing.WaitAsync();
        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }
        finally
        {
            writing.Release();
        }
    }

    public void Close() => stream.Close();
}

/// Listens on a local port for the browser host, which the browser starts for the add-on. Only connections whose
/// first line carries the token saved next to the port are kept, and the file is readable by this user only.
internal sealed class BrowserBridge : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly List<BrowserConnection> connections = [];
    private BrowserEndpoint? endpoint;

    public Action<BrowserConnection>? OnConnected { get; set; }

    public Action<BrowserConnection, BrowserRequest>? OnRequest { get; set; }

    public Action<BrowserConnection>? OnClosed { get; set; }

    public static string Folder => Path.Combine(AppPaths.Data, "Browser");

    public static string EndpointFile => Path.Combine(Folder, "endpoint.json");

    public IReadOnlyList<BrowserConnection> Connections
    {
        get
        {
            lock (connections) return connections.ToList();
        }
    }

    public void Start()
    {
        if (endpoint is not null) return;

        try
        {
            listener.Start();
            endpoint = new BrowserEndpoint(((IPEndPoint)listener.LocalEndpoint).Port, BrowserEndpoint.NewToken());
            Directory.CreateDirectory(Folder);
            File.WriteAllText(EndpointFile, endpoint.ToJson());
            _ = AcceptAsync();
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Browser bridge unavailable: {e.Message}");
        }
    }

    public void Broadcast(string line)
    {
        foreach (var connection in Connections) connection.Send(line);
    }

    public void Dispose()
    {
        listener.Stop();
        foreach (var connection in Connections) connection.Close();
        try
        {
            File.Delete(EndpointFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            var connection = new BrowserConnection(stream);

            try
            {
                using var first = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (await reader.ReadLineAsync(first.Token) is not string hello || endpoint?.Accepts(hello) != true) return;

                lock (connections) connections.Add(connection);
                OnConnected?.Invoke(connection);

                while (await reader.ReadLineAsync() is string line)
                {
                    if (line.Length > 4 * 1024 * 1024) return;
                    if (BrowserRequest.Parse(line) is BrowserRequest request) OnRequest?.Invoke(connection, request);
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
            finally
            {
                bool known;
                lock (connections) known = connections.Remove(connection);
                if (known) OnClosed?.Invoke(connection);
            }
        }
    }
}

/// Tells Chrome, Edge, Brave and Firefox where FlyyLink's browser host is, so the add-on can start it.
internal static class BrowserHosts
{
    private static readonly string[] ChromiumKeys =
    [
        @"Software\Google\Chrome\NativeMessagingHosts",
        @"Software\Chromium\NativeMessagingHosts",
        @"Software\Microsoft\Edge\NativeMessagingHosts",
        @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts",
    ];

    private const string FirefoxKey = @"Software\Mozilla\NativeMessagingHosts";

    public static string HostPath => Path.Combine(AppContext.BaseDirectory, "FlyyLink.BrowserHost.exe");

    /// Writes the manifests and their registry entries again on every launch, so they follow the app when its folder moves.
    public static void Register()
    {
        if (!File.Exists(HostPath)) return;

        try
        {
            Directory.CreateDirectory(BrowserBridge.Folder);
            var chromium = Path.Combine(BrowserBridge.Folder, "chromium.json");
            var firefox = Path.Combine(BrowserBridge.Folder, "firefox.json");
            File.WriteAllText(chromium, Browser.HostManifest(HostPath, firefox: false));
            File.WriteAllText(firefox, Browser.HostManifest(HostPath, firefox: true));

            foreach (var key in ChromiumKeys) Point(key, chromium);
            Point(FirefoxKey, firefox);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Browser host registration failed: {e.Message}");
        }
    }

    private static void Point(string parent, string manifest)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{parent}\{Browser.HostName}");
        key.SetValue("", manifest);
    }
}
