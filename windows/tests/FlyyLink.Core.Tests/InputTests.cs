using System.Text.Json.Nodes;
using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

public class InputTests
{
    [Fact]
    public void EveryInputRoundTrips()
    {
        RemoteInput[] inputs =
        [
            new RemoteInput.Enter(ScreenEdge.Left, 0.25, RemoteInput.Windows),
            new RemoteInput.Leave(ScreenEdge.Bottom, 1),
            new RemoteInput.Release(),
            new RemoteInput.Move(-3.5, 12),
            new RemoteInput.MoveTo(0.5, 0),
            new RemoteInput.Button(MouseButton.Right, true),
            new RemoteInput.Scroll(0, -40),
            new RemoteInput.Key("KeyA", false),
            new RemoteInput.Key("MetaLeft", true),
            new RemoteInput.Key("AudioVolumeUp", true),
            new RemoteInput.Begin(RemoteInput.Android),
            new RemoteInput.Begin(RemoteInput.MacOS, Ready: false),
            new RemoteInput.Text("Héllo 👋"),
        ];

        foreach (var input in inputs)
        {
            Assert.Equal(input, RemoteInput.FromPacket(NetworkPacket.Parse(input.ToPacket().Serialize())));
        }
    }

    [Fact]
    public void RejectsNonsense()
    {
        static NetworkPacket Packet(JsonObject body) => new(PacketType.Input, body);

        Assert.Null(RemoteInput.FromPacket(Packet(new JsonObject { ["action"] = "enter", ["edge"] = "middle", ["position"] = 0.5 })));
        Assert.Null(RemoteInput.FromPacket(Packet(new JsonObject { ["action"] = "moveTo", ["x"] = 1.5, ["y"] = 0.5 })));
        Assert.Null(RemoteInput.FromPacket(Packet(new JsonObject { ["action"] = "move", ["dx"] = 50_000, ["dy"] = 0 })));
        Assert.Null(RemoteInput.FromPacket(Packet(new JsonObject { ["action"] = "key", ["code"] = "Hyper", ["down"] = true })));
        Assert.Null(RemoteInput.FromPacket(Packet(new JsonObject { ["action"] = "button", ["button"] = "left" })));
        Assert.Null(RemoteInput.FromPacket(Packet(new JsonObject { ["action"] = "text", ["text"] = "" })));
        Assert.Null(RemoteInput.FromPacket(Packet(new JsonObject { ["action"] = "text", ["text"] = new string('a', RemoteInput.MaxText + 1) })));
        Assert.Null(RemoteInput.FromPacket(Packet(new JsonObject { ["action"] = "dance" })));
    }

    [Fact]
    public void BeginWithoutReadyMeansReady()
    {
        var packet = new NetworkPacket(PacketType.Input, new JsonObject { ["action"] = "begin", ["platform"] = RemoteInput.Windows });

        Assert.Equal(new RemoteInput.Begin(RemoteInput.Windows, true), RemoteInput.FromPacket(packet));
    }

    [Fact]
    public void EdgesMapPointsBothWays()
    {
        Assert.Equal(ScreenEdge.Right, ScreenEdge.Left.Opposite());
        Assert.Equal(ScreenEdge.Bottom, ScreenEdge.Top.Opposite());

        Assert.Equal(0.25, ScreenEdge.Right.Position(1920, 270, 0, 0, 1920, 1080));
        Assert.Equal(0.0, ScreenEdge.Bottom.Position(-10, 1080, 0, 0, 1920, 1080));
        Assert.Equal((2.0, 450.0), ScreenEdge.Left.Point(0.5, 0, 0, 1440, 900));

        Assert.True(ScreenEdge.Top.Reached(100, 0, 0, 0, 1440, 900));
        Assert.False(ScreenEdge.Right.Reached(1000, 0, 0, 0, 1440, 900));
    }

    [Fact]
    public void KeyNamesCoverTheKeyboard()
    {
        Assert.Contains("KeyZ", KeyCodes.All);
        Assert.Contains("Digit0", KeyCodes.All);
        Assert.Contains("F12", KeyCodes.All);
        Assert.Contains("NumpadEnter", KeyCodes.All);
        Assert.Contains("MediaPlayPause", KeyCodes.All);
        Assert.Equal("MetaLeft", KeyCodes.SwappingControlAndMeta("ControlLeft"));
        Assert.Equal("KeyC", KeyCodes.SwappingControlAndMeta("KeyC"));
    }
}
