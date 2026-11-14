using System.Text.Json.Nodes;
using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

/// Mirrors TvRemoteTests.swift and TvRemoteTest.kt.
public class TvRemoteTests
{
    private static NetworkPacket Wire(NetworkPacket packet) => NetworkPacket.Parse(packet.Serialize());

    [Fact]
    public void KeysAndTextRoundTrip()
    {
        foreach (var key in TvRemote.Keys)
        {
            Assert.Equal(new TvRemote.Key(key), TvRemote.FromPacket(Wire(new TvRemote.Key(key).ToPacket())));
        }
        Assert.Equal(new TvRemote.Text("hello"), TvRemote.FromPacket(Wire(new TvRemote.Text("hello").ToPacket())));
        Assert.Equal("playPause", new TvRemote.Key(TvRemote.PlayPause).ToPacket().GetString("key"));
    }

    [Fact]
    public void PointerRoundTrips()
    {
        Assert.Equal(new TvRemote.Move(12.5, -3), TvRemote.FromPacket(Wire(new TvRemote.Move(12.5, -3).ToPacket())));
        Assert.Equal(new TvRemote.Click(), TvRemote.FromPacket(Wire(new TvRemote.Click().ToPacket())));
        Assert.Equal(new TvRemote.Scroll(0, 40), TvRemote.FromPacket(Wire(new TvRemote.Scroll(0, 40).ToPacket())));
        Assert.Null(TvRemote.FromPacket(Wire(new TvRemote.Move(TvRemote.MaxStep + 1, 0).ToPacket())));
        Assert.Null(TvRemote.FromPacket(new NetworkPacket(PacketType.Remote, new JsonObject { ["action"] = "move", ["dx"] = 1.0 })));
    }

    [Fact]
    public void RejectsNonsense()
    {
        Assert.Null(TvRemote.FromPacket(Wire(new TvRemote.Key("launchMissiles").ToPacket())));
        Assert.Null(TvRemote.FromPacket(Wire(new TvRemote.Text("").ToPacket())));
        Assert.Null(TvRemote.FromPacket(Wire(new TvRemote.Text(new string('x', TvRemote.MaxText + 1)).ToPacket())));
        Assert.Null(TvRemote.FromPacket(new NetworkPacket(PacketType.Remote, new JsonObject { ["action"] = "fly" })));
        Assert.Null(TvRemote.FromPacket(new NetworkPacket(PacketType.Ping)));
    }

    [Fact]
    public void TvsSkipPhoneAndComputerFeatures()
    {
        Assert.Contains(PacketType.Remote, Capabilities.Mvp);
        Assert.Subset(Capabilities.Tv.ToHashSet(), new HashSet<string> { PacketType.Remote, PacketType.ShareRequest, PacketType.Notification, PacketType.MprisRequest, PacketType.FlyyDropRequest });
        Assert.DoesNotContain(PacketType.SmsRequest, Capabilities.Tv);
        Assert.DoesNotContain(PacketType.Telephony, Capabilities.Tv);
        Assert.DoesNotContain(PacketType.Clipboard, Capabilities.Tv);
        Assert.DoesNotContain(PacketType.ScreenRequest, Capabilities.Tv);
        Assert.DoesNotContain(PacketType.Battery, Capabilities.Tv);
        Assert.True(Capabilities.Tv.IsSubsetOf(Capabilities.Mvp));
    }

    [Fact]
    public void CapabilitiesFollowTheDeviceType()
    {
        Assert.Same(Capabilities.Tv, Capabilities.ForType(DeviceType.Tv));
        Assert.Same(Capabilities.Mvp, Capabilities.ForType(DeviceType.Phone));
        Assert.Same(Capabilities.Mvp, Capabilities.ForType(DeviceType.Laptop));
    }
}
