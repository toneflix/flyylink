using System.Text.Json.Nodes;

namespace FlyyLink.Core;

public interface IDeviceDelegate
{
    /// The peer asked to pair. Show `code` and call `Accept()` or `Reject()`.
    void DevicePairRequested(Device device, string code) { }

    void DevicePairStateChanged(Device device, PairState state) { }

    void DeviceReceived(Device device, NetworkPacket packet) { }

    void DeviceDisconnected(Device device) { }
}

/// A connected peer: its encrypted link plus pairing. Packets from a device that is not paired are dropped,
/// except pairing packets and what `guests` lets through. Mirrors Device.kt and Device.swift.
/// With `canPair` false the peer stays unpaired, so only `guests` traffic gets through.
/// Packets of a feature that is off, in `ownFeatures` or the peer's `Features`, go neither way.
public sealed class Device
{
    /// A link with no packets for this long is treated as dead.
    public const long HeartbeatTimeoutSeconds = 30;

    private readonly DeviceCertificate ownCertificate;
    private readonly ITrustStore trustStore;
    private readonly Func<long> clock;
    private readonly IGuestAccess? guests;
    private readonly PairingStateMachine pairing;
    private readonly PayloadTransfer payloads;
    private readonly Func<PhoneFeatures> ownFeatures;
    private readonly object gate = new();
    private long lastReceived;
    private PhoneFeatures peerFeatures;

    public Device(
        DeviceLink link,
        DeviceCertificate ownCertificate,
        ITrustStore trustStore,
        Func<long>? clock = null,
        IGuestAccess? guests = null,
        bool canPair = true,
        Func<PhoneFeatures>? ownFeatures = null)
    {
        Link = link;
        CanPair = canPair;
        this.ownCertificate = ownCertificate;
        this.trustStore = trustStore;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        this.guests = guests;
        pairing = new PairingStateMachine(
            trustStore.IsTrusted(link.Identity.DeviceId) ? PairState.Paired : PairState.NotPaired,
            allowed: canPair);
        payloads = new PayloadTransfer(ownCertificate);
        lastReceived = this.clock();
        this.ownFeatures = ownFeatures ?? (() => PhoneFeatures.All);
        peerFeatures = link.Identity.Features;

        if (!canPair) trustStore.Forget(link.Identity.DeviceId);
    }

    public DeviceLink Link { get; }

    public bool CanPair { get; }

    public IDeviceDelegate? Delegate { get; set; }

    public string Id => Link.Identity.DeviceId;

    public string Name => Link.Identity.DeviceName;

    public PairState PairState
    {
        get
        {
            lock (gate) return pairing.State;
        }
    }

    public bool IsPaired => PairState == PairState.Paired;

    /// The features the peer has on: from its identity, then from each "flyylink.features" it sends.
    public PhoneFeatures Features
    {
        get
        {
            lock (gate) return peerFeatures;
        }
    }

    /// The code to show on screen while a pair request is open.
    public string VerificationCode
    {
        get
        {
            long timestamp;
            lock (gate) timestamp = pairing.RequestTimestamp;

            return VerificationKey.Compute(ownCertificate.PublicKeyDer, Link.PublicKeyDer, timestamp);
        }
    }

    /// Older peers and KDE Connect itself don't send heartbeats, so only expect them from ours.
    private bool SendsHeartbeats => Link.Identity.OutgoingCapabilities.Contains(PacketType.Heartbeat);

    public void Start()
    {
        Link.OnPacket = Handle;
        Link.OnClosed = () => Delegate?.DeviceDisconnected(this);
    }

    public bool Send(NetworkPacket packet)
    {
        if (!MaySend(packet)) return false;

        Link.Send(packet);

        return true;
    }

    /// Sends `packet` with a payload that the peer downloads from us, such as a file.
    /// Returns false when not allowed to send or the payload could not be offered; `completion` then gets the error.
    public bool SendWithPayload(NetworkPacket packet, long size, Stream source, Action<long>? onProgress = null, Action<Exception?>? completion = null)
    {
        if (!MaySend(packet))
        {
            completion?.Invoke(new ProtocolException("not paired"));
            return false;
        }

        try
        {
            var port = payloads.Offer(Link.CertificateDer, source, onProgress, completion);
            packet.PayloadSize = size;
            packet.PayloadTransferInfo = new JsonObject { ["port"] = port };
            Link.Send(packet);

            return true;
        }
        catch (Exception e)
        {
            completion?.Invoke(e);
            return false;
        }
    }

    /// Downloads the payload of a packet from this device into `sink`. `onProgress` gets each piece's byte count.
    public Task ReceivePayloadAsync(NetworkPacket packet, Stream sink, Action<long>? onProgress = null, CancellationToken cancellation = default)
    {
        if (packet.PayloadSize is not long size || packet.PayloadPort is not int port) throw new ProtocolException("packet has no payload");

        return payloads.ReceiveAsync(Link.Host, port, size, Link.CertificateDer, sink, onProgress, cancellation);
    }

    /// Reads the live stream a packet announces, such as the mirrored screen, until the device closes it.
    public Task ReceiveStreamAsync(NetworkPacket packet, Func<ReadOnlyMemory<byte>, ValueTask> onData, CancellationToken cancellation = default)
    {
        if (packet.PayloadSize != PayloadTransfer.Stream || packet.PayloadPort is not int port) throw new ProtocolException("packet has no stream");

        return payloads.StreamAsync(Link.Host, port, Link.CertificateDer, onData, cancellation);
    }

    public void RequestPair() => Run(p => p.RequestPair(clock()));

    public void Accept() => Run(p => p.Accept());

    public void Reject() => Run(p => p.Reject());

    public void Unpair() => Run(p => p.Unpair());

    /// Call every few seconds: expires unanswered pair requests and checks the link is alive.
    public void Tick()
    {
        Run(p => p.Tick(clock()));
        Heartbeat();
    }

    /// Pings the peer, and closes the link if nothing has arrived for a while.
    private void Heartbeat()
    {
        if (!SendsHeartbeats) return;

        long silent;
        lock (gate) silent = clock() - lastReceived;

        if (silent > HeartbeatTimeoutSeconds)
        {
            Link.Close($"no heartbeat for {HeartbeatTimeoutSeconds} seconds");
            return;
        }

        Link.Send(HeartbeatPacket(pong: false));
    }

    private void Handle(NetworkPacket packet)
    {
        var now = clock();
        lock (gate) lastReceived = now;

        if (packet.Type == PacketType.Heartbeat)
        {
            // Answer right away: a sleeping phone may not run its own timer, but it wakes for this.
            if (packet.GetBool("pong") != true) Link.Send(HeartbeatPacket(pong: true));
            return;
        }

        if (packet.Type == PacketType.Pair)
        {
            Run(p => p.Handle(packet, clock()));
            return;
        }

        if (packet.Type == PacketType.Features && IsPaired && PhoneFeatures.FromPacket(packet) is { } features)
        {
            lock (gate) peerFeatures = features;
        }

        if (!ownFeatures().Accepts(packet.Type)) return;

        if (IsPaired || guests?.AdmitsIncoming(Id, packet) == true) Delegate?.DeviceReceived(this, packet);
    }

    private bool MaySend(NetworkPacket packet)
    {
        if (!ownFeatures().Sends(packet.Type) || !Features.Accepts(packet.Type)) return false;

        return IsPaired || guests?.AdmitsOutgoing(Id, packet) == true;
    }

    private void Run(Func<PairingStateMachine, PairAction> step)
    {
        PairAction action;
        PairState state;
        lock (gate)
        {
            action = step(pairing);
            state = pairing.State;
        }

        switch (action.Kind)
        {
            case PairActionKind.None:
                return;
            case PairActionKind.AskUser:
                Delegate?.DevicePairRequested(this, VerificationCode);
                break;
            case PairActionKind.Send:
                Link.Send(action.Packet!);
                break;
        }

        switch (state)
        {
            case PairState.Paired:
                trustStore.Trust(Id, Link.CertificateDer);
                break;
            case PairState.NotPaired:
                trustStore.Forget(Id);
                break;
        }

        Delegate?.DevicePairStateChanged(this, state);
    }

    private static NetworkPacket HeartbeatPacket(bool pong) => new(PacketType.Heartbeat, new JsonObject { ["pong"] = pong });
}
