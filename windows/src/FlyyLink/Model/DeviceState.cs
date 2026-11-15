using FlyyLink.Core;
using FlyyLink.Plugins;

namespace FlyyLink.Model;

/// What the panel shows for one connected device.
public sealed record DeviceState(
    string Id,
    string Name,
    DeviceType Type,
    string Host,
    PairState PairState,
    string? Code,
    BatteryStatus? Battery,
    NowPlaying? Media = null,
    PhoneLocation? Location = null,
    bool CanFlyyDrop = false,
    FlyyDropSend? Outgoing = null,
    CellSignal? Signal = null,
    PhoneFeatures? Features = null)
{
    public bool IsPaired => PairState == PairState.Paired;

    public bool IsComputer => Type.IsComputer();

    public bool IsPhone => Type.IsPhone();

    public bool IsTv => Type == DeviceType.Tv;

    /// Phones show only what they have turned on; other devices have everything.
    public bool Has(PhoneFeature feature) => (Features ?? PhoneFeatures.All).Contains(feature);

    private string Other => IsComputer ? "the other computer" : IsTv ? "the TV" : "your phone";

    /// We asked to pair and wait for the phone.
    public bool IsOurRequest => PairState == PairState.Requested;

    public bool IsTheirRequest => PairState == PairState.RequestedByPeer;

    public string PairingTitle => IsOurRequest ? $"Waiting for {Name}" : $"Pair with {Name}?";

    public string PairingHint => IsOurRequest ? $"Accept on {Other} if the code matches" : $"Same code on {Other}?";

    /// The verification code as separate characters.
    public IReadOnlyList<string> CodeCharacters => (Code ?? "").Where(c => !char.IsWhiteSpace(c)).Select(c => c.ToString()).ToList();

    public int Charge => Battery?.Charge ?? 0;

    public bool IsCharging => Battery?.IsCharging ?? false;

    public bool HasBattery => Battery is not null;

    public bool HasSignal => Signal is not null;

    public int SignalStrength => Signal?.Strength ?? 0;

    public string SignalGeneration => Signal?.Generation ?? "";

    public string BatteryText => Battery is { } battery ? $"{battery.Charge}%" : "–";

    /// Files can go to it with FlyyDrop, without pairing first.
    public bool ShowsFlyyDrop => PairState == PairState.NotPaired && CanFlyyDrop;

    public bool IsFlyyDropWaiting => Outgoing?.Stage == FlyyDropStage.Waiting;

    public bool IsFlyyDropSending => Outgoing?.Stage == FlyyDropStage.Sending;

    public double FlyyDropPercent => (Outgoing?.Progress?.Fraction ?? 0) * 100;

    public Icons.Lucide FlyyDropIcon => IsFlyyDropWaiting ? Icons.Lucide.X : Icons.Lucide.Radar;

    public string FlyyDropHint => IsFlyyDropWaiting ? "Stop waiting" : $"FlyyDrop files to {Name}";

    /// The row's second line: how a FlyyDrop to it is going, or else the usual subtitle.
    public string RowSubtitle => Outgoing?.Status ?? Subtitle;

    public string Subtitle => PairState switch
    {
        PairState.Paired => $"Paired · {Host}",
        PairState.Requested => IsComputer ? "Waiting for the computer…" : IsTv ? "Waiting for the TV…" : "Waiting for the phone…",
        PairState.RequestedByPeer => "Wants to pair",
        _ => Host,
    };
}
