using System.Text;
using System.Text.Json.Nodes;

namespace FlyyLink.Core.Tests;

/// Mirrors BrowserTests.swift.
public class BrowserTests
{
    [Fact]
    public void FramesRoundTripInPieces()
    {
        var bytes = NativeMessaging.Frame("{\"type\":\"hello\"}").Concat(NativeMessaging.Frame("{\"a\":\"é\"}")).ToArray();
        var reader = new NativeMessageReader();

        reader.Append(bytes.AsSpan(0, 3));
        Assert.Null(reader.Next());
        reader.Append(bytes.AsSpan(3));
        Assert.Equal("{\"type\":\"hello\"}", reader.Next());
        Assert.Equal("{\"a\":\"é\"}", reader.Next());
        Assert.Null(reader.Next());
        Assert.Equal(16, BitConverter.ToInt32(bytes, 0));
    }

    [Fact]
    public void ABrokenLengthStopsTheReader()
    {
        var reader = new NativeMessageReader();
        reader.Append(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 1 });

        Assert.Null(reader.Next());
        Assert.True(reader.IsBroken);
    }

    [Fact]
    public void ManifestsNameTheAddOns()
    {
        var chrome = JsonNode.Parse(Browser.HostManifest("/Applications/FlyyLink.app/Contents/MacOS/FlyyLinkBrowserHost", firefox: false))!;
        var firefox = JsonNode.Parse(Browser.HostManifest(@"C:\FlyyLink\FlyyLink.BrowserHost.exe", firefox: true))!;

        Assert.Equal("com.toneflix.flyylink", chrome["name"]!.GetValue<string>());
        Assert.Equal("stdio", chrome["type"]!.GetValue<string>());
        Assert.Equal("chrome-extension://dnlkpekmfbjelcenommpiibkcdbooacj/", chrome["allowed_origins"]![0]!.GetValue<string>());
        Assert.Null(chrome["allowed_extensions"]);
        Assert.Equal(@"C:\FlyyLink\FlyyLink.BrowserHost.exe", firefox["path"]!.GetValue<string>());
        Assert.Equal("browser@flyylink.toneflix.net", firefox["allowed_extensions"]![0]!.GetValue<string>());
    }

    [Fact]
    public void EndpointsCheckTheToken()
    {
        var endpoint = new BrowserEndpoint(52011, "abc123");

        Assert.Equal(endpoint, BrowserEndpoint.Parse(endpoint.ToJson()));
        Assert.True(endpoint.Accepts(endpoint.HelloLine()));
        Assert.False(endpoint.Accepts("{\"token\":\"nope\"}"));
        Assert.False(endpoint.Accepts("not json"));
        Assert.Null(BrowserEndpoint.Parse("{\"port\":70000,\"token\":\"x\"}"));
        Assert.Equal(32, BrowserEndpoint.NewToken().Length);
    }

    [Fact]
    public void CleansNumbers()
    {
        Assert.Equal("+2348031234567", Browser.CleanNumber(" +234 (803) 123-4567 "));
        Assert.Equal("*123#", Browser.CleanNumber("*123#"));
        Assert.Equal("08031234567", Browser.CleanNumber("0803+123 4567"));
        Assert.Null(Browser.CleanNumber("12"));
        Assert.Null(Browser.CleanNumber("call me"));
    }

    [Fact]
    public void ParsesRequests()
    {
        Assert.Equal(new BrowserRequest.Hello("Chrome"), BrowserRequest.Parse("{\"type\":\"hello\",\"browser\":\"Chrome\"}"));
        Assert.Equal(
            new BrowserRequest.Share("phone1", new BrowserShare.Link("https://example.com/a#:~:text=Hello")) { Id = "7" },
            BrowserRequest.Parse("{\"type\":\"share\",\"id\":\"7\",\"device\":\"phone1\",\"link\":\"https://example.com/a#:~:text=Hello\"}"));
        Assert.Equal(
            new BrowserRequest.Share("tv", new BrowserShare.Fetch("https://example.com/cat.jpg", "cat.jpg")),
            BrowserRequest.Parse("{\"type\":\"share\",\"device\":\"tv\",\"fetch\":\"https://example.com/cat.jpg\",\"name\":\"../cat.jpg\"}"));
        Assert.Equal(
            new BrowserRequest.Share("phone1", new BrowserShare.Text("Hello there")),
            BrowserRequest.Parse("{\"type\":\"share\",\"device\":\"phone1\",\"text\":\"Hello there\"}"));
        Assert.Equal(
            new BrowserRequest.Call(null, "+2348031234567"),
            BrowserRequest.Parse("{\"type\":\"call\",\"number\":\"tel:+234 803 123 4567\"}"));
        Assert.Equal(
            new BrowserRequest.Text("phone1", "08031234567", "On my way"),
            BrowserRequest.Parse("{\"type\":\"text\",\"device\":\"phone1\",\"number\":\"0803 123 4567\",\"body\":\"On my way\"}"));
        Assert.Equal(
            new BrowserRequest.PlayOnTv("tv-1", "https://cdn.example.com/v.mp4", "Trailer", "video/mp4"),
            BrowserRequest.Parse("{\"type\":\"playOnTv\",\"tv\":\"tv-1\",\"url\":\"https://cdn.example.com/v.mp4\",\"title\":\"Trailer\",\"mime\":\"video/mp4\"}"));
    }

    [Fact]
    public void RejectsBadRequests()
    {
        Assert.Null(BrowserRequest.Parse("nonsense"));
        Assert.Null(BrowserRequest.Parse("{\"type\":\"share\",\"device\":\"p\",\"link\":\"javascript:alert(1)\"}"));
        Assert.Null(BrowserRequest.Parse("{\"type\":\"share\",\"device\":\"p\",\"file\":\"relative/path.txt\"}"));
        Assert.Null(BrowserRequest.Parse("{\"type\":\"text\",\"number\":\"0803\",\"body\":\"\"}"));
        Assert.Null(BrowserRequest.Parse("{\"type\":\"playOnTv\",\"tv\":\"t\",\"url\":\"blob:https://x/1\"}"));
        Assert.Null(BrowserRequest.Parse("{\"type\":\"launch\"}"));
    }

    [Fact]
    public void WritesEvents()
    {
        var state = JsonNode.Parse(BrowserEvents.State(new BrowserState(
            BrowserState.Ready, "Obi's MacBook", "macos",
            [new BrowserDevice("p1", "Pixel", "phone", true, true)],
            [new BrowserTv("tv1", "Living room")])))!;
        var control = JsonNode.Parse(BrowserEvents.Control("a", MediaAction.PlayPause, 5000))!;

        Assert.Equal("state", state["type"]!.GetValue<string>());
        Assert.Equal("Pixel", state["devices"]![0]!["name"]!.GetValue<string>());
        Assert.True(state["devices"]![0]!["canCall"]!.GetValue<bool>());
        Assert.Equal("Living room", state["tvs"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("PlayPause", control["action"]!.GetValue<string>());
        Assert.Equal(5000, control["seekTo"]!.GetValue<long>());
        Assert.Equal("{\"type\":\"result\",\"id\":\"3\",\"ok\":false,\"error\":\"offline\"}", BrowserEvents.Result("3", false, "offline"));
        Assert.Equal("{\"type\":\"code\",\"code\":\"123456\",\"app\":\"Messages\",\"device\":\"Pixel\"}", BrowserEvents.Code("123456", "Messages", "Pixel"));
    }

    [Fact]
    public void NamesBrowserPlayers()
    {
        var media = new BrowserMedia();
        var line = "{\"type\":\"media\",\"players\":[{\"id\":\"1:0\",\"site\":\"YouTube\",\"title\":\"Song\",\"playing\":true,\"position\":1500.7,\"length\":200000,\"canSeek\":true,\"artwork\":\"https://i.ytimg.com/a.jpg\"},{\"id\":\"2:0\",\"site\":\"YouTube\",\"title\":\"Other\"}]}";
        var players = Assert.IsType<BrowserRequest.Media>(BrowserRequest.Parse(line)).Players;

        media.Update("a", "Chrome", players);
        Assert.Equal(["YouTube in Chrome", "YouTube in Chrome 2"], media.Players.Select(p => p.Name));
        Assert.Equal(1500, media.Players[0].Position);
        Assert.True(media.Players[0].CanSeek);
        Assert.Equal("https://i.ytimg.com/a.jpg", media.Players[0].AlbumArtUrl);
        Assert.Equal(("a", "2:0"), media.Find("YouTube in Chrome 2"));
        Assert.True(media.HasArtwork("https://i.ytimg.com/a.jpg"));

        media.Update("a", "Chrome", [players[1]]);
        Assert.Equal(["YouTube in Chrome 2"], media.Players.Select(p => p.Name));

        media.Update("b", "Firefox", [players[0]]);
        Assert.Equal(("b", "1:0"), media.Find("YouTube in Firefox"));

        media.Forget("a");
        Assert.Equal(["YouTube in Firefox"], media.Players.Select(p => p.Name));
        Assert.Null(media.Find("YouTube in Chrome 2"));
    }
}
