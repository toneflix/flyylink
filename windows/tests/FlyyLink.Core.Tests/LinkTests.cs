using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

public class DeviceCertificateTests
{
    [Fact]
    public void CommonNameIsDeviceId()
    {
        var id = DeviceIdentity.NewDeviceId();

        Assert.Equal(id, DeviceCertificate.Generate(id).DeviceId);
    }

    [Fact]
    public void SurvivesStorage()
    {
        var certificate = DeviceCertificate.Generate(DeviceIdentity.NewDeviceId());

        var loaded = DeviceCertificate.FromPfx(certificate.ExportPfx());

        Assert.Equal(certificate.PublicKeyDer, loaded.PublicKeyDer);
        Assert.True(loaded.Certificate.HasPrivateKey);
    }

    [Fact]
    public void VerificationKeyIsTheSameOnBothSides()
    {
        var a = DeviceCertificate.Generate(DeviceIdentity.NewDeviceId()).PublicKeyDer;
        var b = DeviceCertificate.Generate(DeviceIdentity.NewDeviceId()).PublicKeyDer;

        var code = VerificationKey.Compute(a, b, 1_700_000_000);

        Assert.Equal(code, VerificationKey.Compute(b, a, 1_700_000_000));
        Assert.Equal(8, code.Length);
        Assert.NotEqual(code, VerificationKey.Compute(a, b, 1_700_000_001));
    }

    /// Same inputs as the Kotlin and Swift sides must give the same code.
    [Fact]
    public void VerificationKeyMatchesKotlin()
    {
        Assert.Equal("89CE2D9C", VerificationKey.Compute([1, 2, 3], [0xFF], 42));
        Assert.Equal("89CE2D9C", VerificationKey.Compute([0xFF], [1, 2, 3], 42));
    }
}

/// Collects links and device events so tests can wait on them.
internal sealed class Recorder : IDeviceDelegate
{
    public BlockingCollection<DeviceLink> Links { get; } = [];
    public BlockingCollection<string> Requests { get; } = [];
    public BlockingCollection<NetworkPacket> Packets { get; } = [];
    public BlockingCollection<PairState> Paired { get; } = [];
    public BlockingCollection<bool> Disconnected { get; } = [];

    public void DevicePairRequested(Device device, string code) => Requests.Add(code);

    public void DevicePairStateChanged(Device device, PairState state)
    {
        if (state == PairState.Paired) Paired.Add(state);
    }

    public void DeviceReceived(Device device, NetworkPacket packet) => Packets.Add(packet);

    public void DeviceDisconnected(Device device) => Disconnected.Add(true);
}

internal static class Wait
{
    public static T? Take<T>(this BlockingCollection<T> items, int seconds = 10) =>
        items.TryTake(out var item, TimeSpan.FromSeconds(seconds)) ? item : default;
}

internal sealed class Peer : IDisposable
{
    public Peer(string name, DeviceType type, PortRange ports, int udpPort = 41716, IReadOnlySet<string>? capabilities = null, PhoneFeatures? features = null)
    {
        Identity = new DeviceIdentity
        {
            DeviceId = DeviceIdentity.NewDeviceId(),
            DeviceName = name,
            DeviceType = type,
            IncomingCapabilities = capabilities ?? Capabilities.Mvp,
            OutgoingCapabilities = capabilities ?? Capabilities.Mvp,
            Features = features ?? PhoneFeatures.All,
        };
        Certificate = DeviceCertificate.Generate(Identity.DeviceId);
        var identity = Identity;
        var recorder = Recorder;
        var connected = Connected;

        // High ports so the test does not clash with a real KDE Connect install.
        Lan = new LanLink(() => identity, Certificate, Trust, link => recorder.Links.Add(link), id => connected.ContainsKey(id), udpPort: udpPort, tcpPorts: ports);
    }

    public DeviceIdentity Identity { get; }
    public DeviceCertificate Certificate { get; }
    public InMemoryTrustStore Trust { get; } = new();
    public Recorder Recorder { get; } = new();
    public ConcurrentDictionary<string, bool> Connected { get; } = new();
    public LanLink Lan { get; }

    public DeviceIdentity Announced => Identity with { TcpPort = Lan.TcpPort };

    public Device DeviceFor(DeviceLink link, Func<long>? clock = null, IGuestAccess? guests = null, Func<PhoneFeatures>? features = null)
    {
        var device = new Device(link, Certificate, Trust, clock, guests, ownFeatures: features) { Delegate = Recorder };
        device.Start();

        return device;
    }

    public void Dispose() => Lan.Stop();
}

[Collection("network")]
public sealed class LanLinkTests : IDisposable
{
    private readonly Peer pc = new("PC", DeviceType.Laptop, new PortRange(42716, 42730));
    private readonly Peer phone = new("Phone", DeviceType.Phone, new PortRange(42731, 42764));

    public LanLinkTests() => pc.Lan.Start();

    public void Dispose()
    {
        pc.Dispose();
        phone.Dispose();
    }

    private (DeviceLink OnPhone, DeviceLink OnPc) Connect()
    {
        _ = phone.Lan.ConnectAsync(pc.Announced, "127.0.0.1");

        var onPhone = phone.Recorder.Links.Take() ?? throw new Exception("phone got no link");
        var onPc = pc.Recorder.Links.Take() ?? throw new Exception("PC got no link");

        return (onPhone, onPc);
    }

    [Fact]
    public void ConnectsToAPeerWhoseAnnouncementIsLargerThanTwoKilobytes()
    {
        var capabilities = Enumerable.Range(0, 120).Select(i => $"flyylink.capability.{i}").ToHashSet();
        using var big = new Peer("Phone", DeviceType.Phone, new PortRange(42765, 42780), udpPort: 42799, capabilities: capabilities);
        big.Lan.Start();

        var datagram = big.Announced.ToPacket().Serialize();
        Assert.True(datagram.Length > 4096);

        using var udp = new System.Net.Sockets.UdpClient();
        udp.Send(datagram, datagram.Length, "127.0.0.1", 41716);

        Assert.True(pc.Recorder.Links.TryTake(out _, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void BroadcastsToEveryNetworkAndTheLimitedAddress()
    {
        var addresses = LanLink.BroadcastAddresses();

        Assert.Equal(System.Net.IPAddress.Broadcast, addresses[0]);
        Assert.Equal(addresses.Count, addresses.Distinct().Count());
        Assert.DoesNotContain(addresses, System.Net.IPAddress.IsLoopback);
    }

    private (Device Phone, Device Pc) ConnectPaired()
    {
        var (onPhone, onPc) = Connect();
        phone.Trust.Trust(pc.Identity.DeviceId, onPhone.CertificateDer);
        pc.Trust.Trust(phone.Identity.DeviceId, onPc.CertificateDer);

        return (phone.DeviceFor(onPhone), pc.DeviceFor(onPc));
    }

    [Fact]
    public void FeaturesArriveWithTheIdentityAndWhenTheyChange()
    {
        var phoneHas = PhoneFeatures.Of(PhoneFeature.Clipboard);
        using var featuredPhone = new Peer("Phone", DeviceType.Phone, new PortRange(42781, 42790), features: phoneHas);
        featuredPhone.Trust.Trust(pc.Identity.DeviceId, pc.Certificate.CertificateDer);
        pc.Trust.Trust(featuredPhone.Identity.DeviceId, featuredPhone.Certificate.CertificateDer);
        _ = featuredPhone.Lan.ConnectAsync(pc.Announced, "127.0.0.1");
        var onPhone = featuredPhone.Recorder.Links.Take() ?? throw new Exception("phone got no link");
        var onPc = pc.Recorder.Links.Take() ?? throw new Exception("PC got no link");
        var phoneDevice = featuredPhone.DeviceFor(onPhone, features: () => phoneHas);
        var pcDevice = pc.DeviceFor(onPc);

        Assert.Equal(phoneHas, pcDevice.Features);
        Assert.False(pcDevice.Send(PhoneLocation.Request()));
        Assert.False(phoneDevice.Send(new PhoneLocation(1, 2, 3, 4).ToPacket()));

        onPc.Send(PhoneLocation.Request());
        Assert.True(pcDevice.Send(Clipboard.Packet("hi")));
        Assert.Equal(PacketType.Clipboard, featuredPhone.Recorder.Packets.Take()?.Type);

        phoneHas = PhoneFeatures.Of(PhoneFeature.Clipboard, PhoneFeature.Location);
        Assert.True(phoneDevice.Send(phoneHas.ToPacket()));
        Assert.Equal(PacketType.Features, pc.Recorder.Packets.Take()?.Type);

        Assert.Equal(phoneHas, pcDevice.Features);
        Assert.True(pcDevice.Send(PhoneLocation.Request()));
        Assert.Equal(PacketType.LocationRequest, featuredPhone.Recorder.Packets.Take()?.Type);
    }

    [Fact]
    public void LinkIsEncryptedAndCarriesIdentities()
    {
        var (onPhone, onPc) = Connect();

        Assert.Equal(pc.Identity.DeviceId, onPhone.Identity.DeviceId);
        Assert.Equal(phone.Identity.DeviceId, onPc.Identity.DeviceId);
        Assert.Equal(pc.Identity.DeviceId, DeviceCertificate.CommonName(onPhone.CertificateDer));
        Assert.Equal("PC", onPhone.Identity.DeviceName);
    }

    [Fact]
    public void PairingShowsTheSameCodeAndTrustsBothSides()
    {
        var (onPhone, onPc) = Connect();
        var phoneDevice = phone.DeviceFor(onPhone);
        var pcDevice = pc.DeviceFor(onPc);

        phoneDevice.RequestPair();
        var codeOnPc = pc.Recorder.Requests.Take();

        Assert.Equal(phoneDevice.VerificationCode, codeOnPc);

        pcDevice.Accept();

        Assert.Equal(PairState.Paired, phone.Recorder.Paired.Take());
        Assert.True(((ITrustStore)phone.Trust).IsTrusted(pc.Identity.DeviceId));
        Assert.True(((ITrustStore)pc.Trust).IsTrusted(phone.Identity.DeviceId));

        Assert.True(phoneDevice.Send(new NetworkPacket(PacketType.Ping)));
        Assert.Equal(PacketType.Ping, pc.Recorder.Packets.Take()?.Type);
    }

    [Fact]
    public async Task FlyyDropMovesOnlyTheAcceptedFileBetweenUnpairedDevices()
    {
        var (onPhone, onPc) = Connect();
        var phoneDrop = new FlyyDrop();
        var pcDrop = new FlyyDrop { ReceivingMode = FlyyDrop.Receiving.Everyone };
        var phoneDevice = phone.DeviceFor(onPhone, guests: phoneDrop);
        var pcDevice = pc.DeviceFor(onPc, guests: pcDrop);
        var bytes = "hello from the phone"u8.ToArray();

        Assert.False(phoneDevice.Send(new NetworkPacket(PacketType.Ping)));
        var item = new FlyyDrop.Item("hello.txt", bytes.Length);
        Assert.True(phoneDevice.Send(phoneDrop.Request(pc.Identity.DeviceId, [item])));

        var request = pc.Recorder.Packets.Take()!;
        var offer = Assert.IsType<FlyyDrop.Incoming.Ask>(pcDrop.OnRequest(phone.Identity.DeviceId, request, isPaired: false)).Offer;
        Assert.True(pcDevice.Send(pcDrop.Accept(offer.Id)!));
        Assert.True(phoneDrop.OnResponse(pc.Identity.DeviceId, phone.Recorder.Packets.Take()!)!.Accepted);

        Assert.True(phoneDevice.SendWithPayload(FlyyDrop.File(offer.Id, item), item.Size, new MemoryStream(bytes)));
        var file = pc.Recorder.Packets.Take()!;
        var received = new MemoryStream();
        await pcDevice.ReceivePayloadAsync(file, received);

        Assert.Equal("hello from the phone", System.Text.Encoding.UTF8.GetString(received.ToArray()));
        Assert.False(phoneDevice.SendWithPayload(FlyyDrop.File(offer.Id, item), item.Size, new MemoryStream(bytes)));
        Assert.False(((ITrustStore)pc.Trust).IsTrusted(phone.Identity.DeviceId));
    }

    [Fact]
    public void ChangedCertificateIsRejected()
    {
        var other = DeviceCertificate.Generate(phone.Identity.DeviceId);
        pc.Trust.Trust(phone.Identity.DeviceId, other.CertificateDer);

        _ = phone.Lan.ConnectAsync(pc.Announced, "127.0.0.1");

        Assert.Null(pc.Recorder.Links.Take(seconds: 3));
    }

    [Fact]
    public void BothSidesKeepTheSameLinkWhenTheyConnectAtOnce()
    {
        phone.Lan.Start();

        _ = phone.Lan.ConnectAsync(pc.Announced, "127.0.0.1");
        var phoneOwn = phone.Recorder.Links.Take()!;
        var pcFromPhone = pc.Recorder.Links.Take()!;

        _ = pc.Lan.ConnectAsync(phone.Announced, "127.0.0.1");
        var pcOwn = pc.Recorder.Links.Take()!;
        var phoneFromPc = phone.Recorder.Links.Take()!;

        // Each side sees its own link first.
        var phoneKeepsItsOwn = DeviceLink.KeepsExisting(phoneOwn, phoneFromPc, phone.Identity.DeviceId);
        var pcKeepsItsOwn = DeviceLink.KeepsExisting(pcOwn, pcFromPhone, pc.Identity.DeviceId);

        Assert.NotEqual(phoneKeepsItsOwn, pcKeepsItsOwn);
    }

    [Fact]
    public void ReconnectReplacesTheOldLink()
    {
        var (first, _) = Connect();
        var (second, _) = Connect();

        Assert.False(DeviceLink.KeepsExisting(first, second, phone.Identity.DeviceId));
    }

    [Fact]
    public void HeartbeatIsAnswered()
    {
        var (onPhone, onPc) = Connect();
        pc.DeviceFor(onPc);
        var received = new BlockingCollection<NetworkPacket>();
        onPhone.OnPacket = received.Add;

        onPhone.Send(new NetworkPacket(PacketType.Heartbeat, new JsonObject { ["pong"] = false }));

        var answer = received.Take();
        Assert.Equal(PacketType.Heartbeat, answer?.Type);
        Assert.True(answer?.GetBool("pong"));
    }

    [Fact]
    public void SilentLinkIsClosed()
    {
        var (onPhone, _) = Connect();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var device = phone.DeviceFor(onPhone, () => now);

        now += Device.HeartbeatTimeoutSeconds + 1;
        device.Tick();

        Assert.True(phone.Recorder.Disconnected.Take());
        Assert.Contains("heartbeat", onPhone.CloseReason);
    }

    [Fact]
    public async Task FileTravelsOverItsOwnTlsConnection()
    {
        var (phoneDevice, pcDevice) = ConnectPaired();
        var bytes = new byte[3 * 1024 * 1024 + 17];
        Random.Shared.NextBytes(bytes);
        var sent = new BlockingCollection<Exception?>();

        Assert.True(phoneDevice.SendWithPayload(Share.FilePacket("photo.jpg", bytes.Length), bytes.Length, new MemoryStream(bytes), completion: sent.Add));

        var offer = pc.Recorder.Packets.Take()!;
        Assert.Equal(new Share.File("photo.jpg", bytes.Length), Share.FromPacket(offer));

        var sink = new MemoryStream();
        long progress = 0;
        await pcDevice.ReceivePayloadAsync(offer, sink, n => Interlocked.Add(ref progress, n)).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(bytes, sink.ToArray());
        Assert.Equal(bytes.Length, Interlocked.Read(ref progress));
        Assert.True(sent.TryTake(out var error, TimeSpan.FromSeconds(10)));
        Assert.Null(error);
    }

    [Fact]
    public async Task LiveStreamIsReadUntilTheSenderCloses()
    {
        var (phoneDevice, pcDevice) = ConnectPaired();
        var bytes = new byte[100_000];
        Random.Shared.NextBytes(bytes);

        var packet = new NetworkPacket(PacketType.Screen);
        phoneDevice.SendWithPayload(packet, PayloadTransfer.Stream, new MemoryStream(bytes));
        var announced = pc.Recorder.Packets.Take()!;

        var received = new MemoryStream();
        await pcDevice.ReceiveStreamAsync(announced, data =>
        {
            received.Write(data.Span);
            return ValueTask.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(bytes, received.ToArray());
    }

    [Fact]
    public async Task LiveStreamSendsPiecesAsTheyAreWritten()
    {
        var (phoneDevice, pcDevice) = ConnectPaired();
        var live = new LiveStream(backlog: 8);
        var ended = new BlockingCollection<Exception?>();

        Assert.True(phoneDevice.SendWithPayload(Screen.Started(1440, 900, codec: Screen.Jpeg), PayloadTransfer.Stream, live, completion: ended.Add));
        var announced = pc.Recorder.Packets.Take()!;

        var received = new MemoryStream();
        var reading = pcDevice.ReceiveStreamAsync(announced, data =>
        {
            received.Write(data.Span);
            return ValueTask.CompletedTask;
        });
        var frames = new[] { new ScreenFrame([1, 2, 3], 0, ScreenFrame.Key), new ScreenFrame(new byte[70_000], 66_666, ScreenFrame.Key) };
        foreach (var frame in frames) Assert.True(live.Push([.. frame.Header(), .. frame.Data]));
        live.Complete();
        await reading.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(frames, new ScreenFrameReader().Feed(received.ToArray()).ToArray());
        Assert.True(ended.TryTake(out var error, TimeSpan.FromSeconds(10)));
        Assert.Null(error);
        Assert.False(live.Push([4]));
    }

    [Fact]
    public void AnnouncementConnectsToANewPeer()
    {
        Assert.True(phone.Lan.HandleAnnouncement(pc.Announced, "127.0.0.1"));
        Assert.NotNull(phone.Recorder.Links.Take());
    }

    [Fact]
    public void AnnouncementKeepsTheExistingLink()
    {
        phone.Connected[pc.Identity.DeviceId] = true;

        Assert.False(phone.Lan.HandleAnnouncement(pc.Announced, "127.0.0.1"));
        Assert.False(phone.Lan.HandleAnnouncement(phone.Identity, "127.0.0.1"));
    }
}

[CollectionDefinition("network", DisableParallelization = true)]
public sealed class NetworkCollection;
