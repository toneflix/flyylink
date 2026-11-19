using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FlyyLink.Core.Tests;

/// Mirrors BrowserHostRelayTests.swift.
public class BrowserHostRelayTests
{
    [Fact(Timeout = 10_000)]
    public async Task PassesMessagesBothWays()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        BrowserEndpoint? endpoint = null;

        using var toHost = new AnonymousPipeServerStream(PipeDirection.Out);
        using var hostIn = new AnonymousPipeClientStream(PipeDirection.In, toHost.ClientSafePipeHandle);
        using var fromHost = new AnonymousPipeServerStream(PipeDirection.In);
        using var hostOut = new AnonymousPipeClientStream(PipeDirection.Out, fromHost.ClientSafePipeHandle);

        var launched = new TaskCompletionSource();
        var relay = new BrowserHostRelay(hostIn, hostOut, () => endpoint) { RetryDelay = TimeSpan.FromMilliseconds(50), Launch = () => launched.TrySetResult() };
        var running = relay.RunAsync();

        Assert.Equal(BrowserEvents.Closed(), await ReadFrameAsync(fromHost));

        await toHost.WriteAsync(NativeMessaging.Frame("{\"type\":\"launch\"}"));
        await launched.Task;
        await toHost.WriteAsync(NativeMessaging.Frame("{\"type\":\"hello\",\"browser\":\"Chrome\"}"));
        endpoint = new BrowserEndpoint(port, "secret");

        using var app = await listener.AcceptTcpClientAsync();
        var appIn = new StreamReader(app.GetStream());
        Assert.Equal("{\"token\":\"secret\"}", await appIn.ReadLineAsync());
        Assert.Equal("{\"type\":\"hello\",\"browser\":\"Chrome\"}", await appIn.ReadLineAsync());

        await toHost.WriteAsync(NativeMessaging.Frame("{\"type\":\"call\",\"number\":\"123\"}"));
        Assert.Equal("{\"type\":\"call\",\"number\":\"123\"}", await appIn.ReadLineAsync());

        await app.GetStream().WriteAsync(Encoding.UTF8.GetBytes("{\"type\":\"call\",\"active\":true}\n"));
        Assert.Equal("{\"type\":\"call\",\"active\":true}", await ReadFrameAsync(fromHost));

        endpoint = null;
        app.Close();
        Assert.Equal(BrowserEvents.Closed(), await ReadFrameAsync(fromHost));

        toHost.Close();
        await running;
        listener.Stop();
    }

    private static async Task<string> ReadFrameAsync(Stream stream)
    {
        var reader = new NativeMessageReader();
        var chunk = new byte[1];

        while (true)
        {
            if (reader.Next() is string message) return message;

            Assert.Equal(1, await stream.ReadAsync(chunk));
            reader.Append(chunk);
        }
    }
}
