using System.Text.Json.Nodes;
using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

/// Mirrors FeaturesTest.kt and FeaturesTests.swift.
public class FeaturesTests
{
    private static NetworkPacket Wire(NetworkPacket packet) => NetworkPacket.Parse(packet.Serialize());

    [Fact]
    public void UnreportedFeaturesAreAllOn()
    {
        Assert.All(Enum.GetValues<PhoneFeature>(), f => Assert.True(PhoneFeatures.All.Contains(f)));
        Assert.True(PhoneFeatures.All.Sends(PacketType.SmsMessages));
        Assert.True(PhoneFeatures.All.Accepts(PacketType.ScreenInput));
    }

    [Fact]
    public void FeaturesThatAreOffBlockTheirPackets()
    {
        var features = PhoneFeatures.Of(PhoneFeature.Clipboard, PhoneFeature.Mirroring);

        Assert.True(features.Sends(PacketType.Clipboard));
        Assert.True(features.Accepts(PacketType.ScreenRequest));
        Assert.False(features.Sends(PacketType.Notification));
        Assert.False(features.Accepts(PacketType.SmsRequestConversations));
        Assert.False(features.Accepts(PacketType.ScreenInput));
    }

    [Fact]
    public void PacketsOfNoFeatureAlwaysGo()
    {
        var none = PhoneFeatures.Of();

        foreach (var type in new[] { PacketType.Ping, PacketType.ShareRequest, PacketType.FindMyPhone, PacketType.Battery, PacketType.Features })
        {
            Assert.True(none.Sends(type));
            Assert.True(none.Accepts(type));
        }
    }

    [Fact]
    public void PhonesTakeComputerNotificationsWithTheirOwnOff()
    {
        var none = PhoneFeatures.Of();

        Assert.True(none.Accepts(PacketType.Notification));
        Assert.True(none.Sends(PacketType.NotificationRequest));
    }

    [Fact]
    public void PacketRoundTripsAndSkipsUnknownNames()
    {
        var features = PhoneFeatures.Of(PhoneFeature.Photos, PhoneFeature.Location);

        Assert.Equal(features, PhoneFeatures.FromPacket(Wire(features.ToPacket())));

        var future = new NetworkPacket(PacketType.Features, new JsonObject { ["features"] = new JsonArray("photos", "teleport") });
        Assert.Equal(PhoneFeatures.Of(PhoneFeature.Photos), PhoneFeatures.FromPacket(future));
        Assert.Null(PhoneFeatures.FromPacket(new NetworkPacket(PacketType.Features)));
        Assert.Null(PhoneFeatures.FromPacket(Clipboard.Packet("hi")));
    }

    [Fact]
    public void IdentityCarriesFeaturesButTheAnnouncementDoesNot()
    {
        var identity = new DeviceIdentity
        {
            DeviceId = DeviceIdentity.NewDeviceId(),
            DeviceName = "Pixel 9",
            DeviceType = DeviceType.Phone,
            TcpPort = 1716,
            Features = PhoneFeatures.Of(PhoneFeature.Notifications),
        };

        Assert.Equal(identity, DeviceIdentity.FromPacket(Wire(identity.ToPacket())));
        Assert.Equal(PhoneFeatures.All, DeviceIdentity.FromPacket(Wire(identity.Announcement())).Features);
    }

    [Fact]
    public void NothingOnIsNotTheSameAsUnreported()
    {
        var identity = new DeviceIdentity { DeviceId = DeviceIdentity.NewDeviceId(), DeviceName = "Pixel 9", DeviceType = DeviceType.Phone, Features = PhoneFeatures.Of() };
        var heard = DeviceIdentity.FromPacket(Wire(identity.ToPacket()));

        Assert.Equal(PhoneFeatures.Of(), heard.Features);
        Assert.False(heard.Features.Contains(PhoneFeature.Files));
    }

    /// The same names as Features.kt and Features.swift.
    [Fact]
    public void WireNamesMatchTheOtherApps()
    {
        Assert.Equal(
            ["notifications", "media", "messages", "clipboard", "photos", "files", "mirroring", "control", "location"],
            Enum.GetValues<PhoneFeature>().Select(f => f.WireName()));
    }
}
