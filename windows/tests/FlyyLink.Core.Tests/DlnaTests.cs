using System.Net;
using System.Net.Sockets;
using System.Text;
using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

/// Mirrors DlnaTests.swift and DlnaTest.kt.
public class DlnaTests
{
    private const string Description = """
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <device>
            <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
            <friendlyName>Living Room TV</friendlyName>
            <UDN>uuid:1234-abcd</UDN>
            <serviceList>
              <service>
                <serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType>
                <controlURL>/upnp/control/RenderingControl1</controlURL>
              </service>
              <service>
                <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
                <controlURL>control/AVTransport1</controlURL>
              </service>
            </serviceList>
          </device>
        </root>
        """;

    [Fact]
    public void SearchMessageAsksForRenderers()
    {
        var message = Ssdp.SearchMessage();

        Assert.StartsWith("M-SEARCH * HTTP/1.1\r\n", message);
        Assert.Contains("ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n", message);
        Assert.Contains("MAN: \"ssdp:discover\"\r\n", message);
        Assert.EndsWith("\r\n\r\n", message);
    }

    [Fact]
    public void ReadsSearchReplies()
    {
        var reply = Ssdp.Parse(
            "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nLocation: http://192.168.1.20:9197/dmr\r\n" +
            "ST: urn:schemas-upnp-org:device:MediaRenderer:1\r\nUSN: uuid:1234-abcd::urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n");

        Assert.Equal(new SsdpReply("http://192.168.1.20:9197/dmr", "uuid:1234-abcd::urn:schemas-upnp-org:device:MediaRenderer:1", Ssdp.Renderer), reply);
    }

    [Fact]
    public void IgnoresSearchesAndByeNotices()
    {
        Assert.Null(Ssdp.Parse(Ssdp.SearchMessage()));
        Assert.Null(Ssdp.Parse("NOTIFY * HTTP/1.1\r\nLOCATION: http://192.168.1.20/dmr\r\nNTS: ssdp:byebye\r\n\r\n"));
        Assert.Null(Ssdp.Parse("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\n\r\n"));
    }

    [Fact]
    public void ReadsRendererDescription()
    {
        var tv = TvRenderer.Parse(Description, "http://192.168.1.20:9197/dmr/desc.xml");

        Assert.Equal(
            new TvRenderer(
                "uuid:1234-abcd",
                "Living Room TV",
                "http://192.168.1.20:9197/dmr/control/AVTransport1",
                "http://192.168.1.20:9197/upnp/control/RenderingControl1"),
            tv);
        Assert.Equal("192.168.1.20", tv?.Host);
    }

    [Fact]
    public void UsesUrlBaseAndNestedDevices()
    {
        const string xml = """
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <URLBase>http://10.0.0.5:8080/</URLBase>
              <device>
                <friendlyName>Hub</friendlyName>
                <UDN>uuid:hub</UDN>
                <deviceList>
                  <device>
                    <friendlyName>Hisense TV</friendlyName>
                    <UDN>uuid:tv</UDN>
                    <serviceList>
                      <service>
                        <serviceType>urn:schemas-upnp-org:service:AVTransport:2</serviceType>
                        <controlURL>/av</controlURL>
                      </service>
                    </serviceList>
                  </device>
                </deviceList>
              </device>
            </root>
            """;

        Assert.Equal(new TvRenderer("uuid:tv", "Hisense TV", "http://10.0.0.5:8080/av"), TvRenderer.Parse(xml, "http://10.0.0.5:1400/desc.xml"));
    }

    [Fact]
    public void SkipsDevicesThatCantPlay()
    {
        Assert.Null(TvRenderer.Parse(Description.Replace("AVTransport", "ConnectionManager"), "http://192.168.1.20/desc.xml"));
        Assert.Null(TvRenderer.Parse("not xml", "http://192.168.1.20/desc.xml"));
    }

    [Fact]
    public void PicksKindFromMime()
    {
        Assert.Equal(CastKind.Photo, CastKinds.Of("image/jpeg"));
        Assert.Equal(CastKind.Video, CastKinds.Of("video/mp4"));
        Assert.Equal(CastKind.Music, CastKinds.Of("audio/mpeg"));
        Assert.Null(CastKinds.Of("application/pdf"));
        Assert.Null(CastKinds.Of(null));
    }

    [Fact]
    public void BuildsSoapEnvelope()
    {
        var call = AvTransport.Seek(3_723_000);

        Assert.Equal("\"urn:schemas-upnp-org:service:AVTransport:1#Seek\"", call.SoapAction);
        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            "<s:Body><u:Seek xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">" +
            "<InstanceID>0</InstanceID><Unit>REL_TIME</Unit><Target>1:02:03</Target>" +
            "</u:Seek></s:Body></s:Envelope>",
            call.Envelope());
    }

    [Fact]
    public void EscapesMetadataInsideTheEnvelope()
    {
        var call = AvTransport.SetUri("http://192.168.1.5:5000/t/a%20b.mp4", "Tom & Jerry <1>", "video/mp4", 2048);
        var metadata = call.Argument("CurrentURIMetaData")!;

        Assert.Contains("<dc:title>Tom &amp; Jerry &lt;1&gt;</dc:title>", metadata);
        Assert.Contains("<upnp:class>object.item.videoItem</upnp:class>", metadata);
        Assert.Contains("<res protocolInfo=\"http-get:*:video/mp4:*\" size=\"2048\">http://192.168.1.5:5000/t/a%20b.mp4</res>", metadata);
        Assert.Contains("<CurrentURIMetaData>&lt;DIDL-Lite ", call.Envelope());
        Assert.Contains("Tom &amp;amp; Jerry", call.Envelope());
    }

    [Fact]
    public void ReadsPositionAndState()
    {
        const string position = """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>
            <u:GetPositionInfoResponse xmlns:u="urn:schemas-upnp-org:service:AVTransport:1">
            <Track>1</Track><TrackDuration>0:04:03</TrackDuration><RelTime>0:01:01.500</RelTime>
            </u:GetPositionInfoResponse></s:Body></s:Envelope>
            """;
        const string state = """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>
            <u:GetTransportInfoResponse xmlns:u="urn:schemas-upnp-org:service:AVTransport:1">
            <CurrentTransportState>PAUSED_PLAYBACK</CurrentTransportState>
            </u:GetTransportInfoResponse></s:Body></s:Envelope>
            """;
        var unknown = position.Replace("0:04:03", "NOT_IMPLEMENTED").Replace("0:01:01.500", "0:00:00");

        Assert.Equal(new CastPosition(61_500, 243_000), AvTransport.Position(position));
        Assert.Equal(new CastPosition(0, 0), AvTransport.Position(unknown));
        Assert.Equal(CastState.Paused, AvTransport.State(state));
        Assert.Equal(CastState.Stopped, CastStates.FromWire("NO_MEDIA_PRESENT"));
    }

    [Fact]
    public void FormatsAndParsesTimes()
    {
        Assert.Equal("0:00:00", AvTransport.Time(-5));
        Assert.Equal("0:01:05", AvTransport.Time(65_900));
        Assert.Equal("12:00:00", AvTransport.Time(43_200_000));
        Assert.Equal(3_723_000, AvTransport.ParseTime("1:02:03"));
        Assert.Equal(3_723_250, AvTransport.ParseTime("01:02:03.25"));
        Assert.Null(AvTransport.ParseTime("NOT_IMPLEMENTED"));
    }

    [Fact]
    public void ReadsAndSetsVolume()
    {
        const string response = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetVolumeResponse xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\"><CurrentVolume>23</CurrentVolume></u:GetVolumeResponse></s:Body></s:Envelope>";

        Assert.Equal(23, RenderingControl.Volume(response));
        Assert.Equal(
            new SoapCall(RenderingControl.Service, "SetVolume", [new("InstanceID", "0"), new("Channel", "Master"), new("DesiredVolume", "100")]),
            RenderingControl.SetVolume(140));
    }

    [Fact]
    public void ParsesRanges()
    {
        Assert.Null(ByteRange.Parse(null, 1000));
        Assert.Null(ByteRange.Parse("items=0-1", 1000));
        Assert.Null(ByteRange.Parse("bytes=0-1,5-6", 1000));
        Assert.Null(ByteRange.Parse("bytes=9-3", 1000));
        Assert.Equal(new ByteRange(0, 999), ByteRange.Parse("bytes=0-", 1000));
        Assert.Equal(new ByteRange(100, 199), ByteRange.Parse("bytes=100-199", 1000));
        Assert.Equal(new ByteRange(900, 999), ByteRange.Parse("bytes=900-5000", 1000));
        Assert.Equal(new ByteRange(800, 999), ByteRange.Parse("bytes=-200", 1000));
        Assert.Equal(new ByteRange(0, 999), ByteRange.Parse("bytes=-5000", 1000));
        Assert.Equal(100, ByteRange.Parse("bytes=100-199", 1000)?.Length);
        Assert.False(ByteRange.Parse("bytes=1000-", 1000)!.IsSatisfiable);
    }

    [Fact]
    public void ParsesRequestHead()
    {
        var head = HttpRequestHead.Parse("GET /abc/clip.mp4 HTTP/1.1\r\nHost: 1.2.3.4\r\nRange: bytes=0-\r\n\r\n");

        Assert.Equal(new HttpRequestHead("GET", "/abc/clip.mp4", new Dictionary<string, string> { ["host"] = "1.2.3.4", ["range"] = "bytes=0-" }), head);
        Assert.Null(HttpRequestHead.Parse(""));
    }

    [Fact]
    public async Task ServerHandsOutWholeFilesAndRanges()
    {
        var bytes = Enumerable.Range(0, 10_000).Select(i => (byte)(i % 251)).ToArray();
        var folder = Directory.CreateTempSubdirectory();
        var path = Path.Combine(folder.FullName, "clip one.mp4");
        await File.WriteAllBytesAsync(path, bytes);

        try
        {
            using var server = new MediaServer();
            using var http = new HttpClient();

            var url = server.Share(new MediaFile(path, "video/mp4"), "127.0.0.1");
            Assert.EndsWith("/clip%20one.mp4", url);

            using var whole = await http.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
            Assert.Equal("Streaming", whole.Headers.GetValues("transferMode.dlna.org").Single());
            Assert.Equal(bytes, await whole.Content.ReadAsByteArrayAsync());

            using var ranged = new HttpRequestMessage(HttpMethod.Get, url);
            ranged.Headers.TryAddWithoutValidation("Range", "bytes=9000-");
            using var part = await http.SendAsync(ranged);
            Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
            Assert.Equal("bytes 9000-9999/10000", part.Content.Headers.ContentRange?.ToString());
            Assert.Equal(bytes[9000..], await part.Content.ReadAsByteArrayAsync());

            using var past = new HttpRequestMessage(HttpMethod.Get, url);
            past.Headers.TryAddWithoutValidation("Range", "bytes=20000-");
            using var tooFar = await http.SendAsync(past);
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, tooFar.StatusCode);

            server.Clear();
            using var gone = await http.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlayerSendsActionsToTheTv()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var actions = new List<string>();
        var bodies = new List<string>();

        var fake = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                var (head, body) = await ReadRequestAsync(client.GetStream());
                actions.Add(head.Headers["soapaction"]);
                bodies.Add(body);

                var reply = $"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetTransportInfoResponse xmlns:u=\"{AvTransport.Service}\"><CurrentTransportState>PLAYING</CurrentTransportState></u:GetTransportInfoResponse></s:Body></s:Envelope>";
                var bytes = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(reply)}\r\nConnection: close\r\n\r\n{reply}");
                await client.GetStream().WriteAsync(bytes);
            }
        });

        try
        {
            var player = new TvPlayer(new TvRenderer("uuid:tv", "TV", $"http://127.0.0.1:{port}/av"));

            await player.PlayAsync("http://127.0.0.1/x.jpg", "x.jpg", "image/jpeg");
            Assert.Equal(CastState.Playing, await player.StateAsync());
            Assert.Null(await player.VolumeAsync());
            await fake;

            Assert.Equal(new[] { "SetAVTransportURI", "Play", "GetTransportInfo" }.Select(a => $"\"{AvTransport.Service}#{a}\""), actions);
            Assert.Contains("object.item.imageItem.photo", bodies[0]);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void KeepsTvsUntilTheyMissTwoSearches()
    {
        var living = new TvRenderer("uuid:a", "Living Room", "http://10.0.0.2/av");
        var bedroom = new TvRenderer("uuid:b", "bedroom", "http://10.0.0.3/av");
        var list = new TvList();

        Assert.Equal([bedroom, living], list.Update([living, bedroom]));
        Assert.Equal([bedroom, living], list.Update([living]));
        Assert.Equal([bedroom, living], list.Update([living, bedroom]));
        Assert.Equal([bedroom, living], list.Update([]));
        Assert.Empty(list.Update([]));
    }

    private static async Task<(HttpRequestHead Head, string Body)> ReadRequestAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];

        while (!(bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n'))
        {
            await stream.ReadExactlyAsync(one);
            bytes.Add(one[0]);
        }

        var head = HttpRequestHead.Parse(Encoding.ASCII.GetString(bytes.ToArray()))!;
        var body = new byte[int.Parse(head.Headers["content-length"])];
        await stream.ReadExactlyAsync(body);

        return (head, Encoding.UTF8.GetString(body));
    }
}
