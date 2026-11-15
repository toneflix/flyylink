using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// One SIM's mobile signal: `NetworkType` as KDE Connect names it ("LTE", "5G", "UMTS"...) and `Strength` from 0 to 4 bars.
/// Mirrors Connectivity.kt and Connectivity.swift.
public sealed record CellSignal(string NetworkType, int Strength)
{
    public const int MaxStrength = 4;

    public const string Nr = "5G";
    public const string Lte = "LTE";
    public const string Hspa = "HSPA";
    public const string Umts = "UMTS";
    public const string Cdma2000 = "CDMA2000";
    public const string Edge = "EDGE";
    public const string Gprs = "GPRS";
    public const string Gsm = "GSM";
    public const string Cdma = "CDMA";
    public const string Iden = "iDEN";
    public const string Unknown = "Unknown";

    /// The short name a status bar shows, such as "4G", or "" when unknown.
    public string Generation => NetworkType switch
    {
        Nr => "5G",
        Lte => "4G",
        Hspa => "H",
        Umts or Cdma2000 => "3G",
        Edge => "E",
        Gprs or Gsm or Cdma or Iden => "2G",
        _ => "",
    };
}

/// The phone's mobile signal for each SIM ("kdeconnect.connectivity_report"), keyed by subscription id.
public sealed record ConnectivityReport(IReadOnlyDictionary<string, CellSignal> Signals)
{
    /// The SIM a computer shows: the one with the most bars.
    public CellSignal? Best => Signals.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).MaxBy(signal => signal.Strength);

    public static NetworkPacket Request() => new(PacketType.ConnectivityReportRequest);

    public static ConnectivityReport? FromPacket(NetworkPacket packet)
    {
        if (packet.Type != PacketType.ConnectivityReport) return null;

        var signals = new Dictionary<string, CellSignal>();
        if (packet.Body["signalStrengths"] is not JsonObject strengths) return new ConnectivityReport(signals);

        foreach (var (id, value) in strengths)
        {
            if (value is not JsonObject entry || entry["signalStrength"].AsLong() is not long strength) continue;

            signals[id] = new CellSignal(entry["networkType"].AsString() ?? CellSignal.Unknown, (int)Math.Clamp(strength, 0, CellSignal.MaxStrength));
        }

        return new ConnectivityReport(signals);
    }

    public NetworkPacket ToPacket()
    {
        var strengths = new JsonObject();
        foreach (var (id, signal) in Signals)
        {
            strengths[id] = new JsonObject { ["networkType"] = signal.NetworkType, ["signalStrength"] = signal.Strength };
        }

        return new NetworkPacket(PacketType.ConnectivityReport, new JsonObject { ["signalStrengths"] = strengths });
    }

    public bool Matches(ConnectivityReport other) =>
        Signals.Count == other.Signals.Count && Signals.All(pair => other.Signals.TryGetValue(pair.Key, out var signal) && signal == pair.Value);
}
