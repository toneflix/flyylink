using FlyyLink.Core;

namespace FlyyLink.Core.Tests;

/// ConnectivityTest.kt and ConnectivityTests.swift check the same.
public class ConnectivityTests
{
    [Fact]
    public void ReadsKdeConnectivityReport()
    {
        var packet = NetworkPacket.Parse("""{"id":1,"type":"kdeconnect.connectivity_report","body":{"signalStrengths":{"6":{"networkType":"LTE","signalStrength":3},"7":{"networkType":"GSM","signalStrength":9}}}}""");

        var report = ConnectivityReport.FromPacket(packet)!;

        Assert.Equal(new CellSignal("LTE", 3), report.Signals["6"]);
        Assert.Equal(new CellSignal("GSM", 4), report.Signals["7"]);
        Assert.Equal(new CellSignal("GSM", 4), report.Best);
    }

    [Fact]
    public void RoundTripsAndPicksTheStrongestSim()
    {
        var report = new ConnectivityReport(new Dictionary<string, CellSignal> { ["1"] = new(CellSignal.Nr, 2), ["2"] = new(CellSignal.Lte, 2) });

        var back = ConnectivityReport.FromPacket(NetworkPacket.Parse(report.ToPacket().Serialize()))!;

        Assert.True(report.Matches(back));
        Assert.Equal(new CellSignal(CellSignal.Nr, 2), back.Best);
    }

    [Fact]
    public void EmptyReportHasNoSignal()
    {
        var report = ConnectivityReport.FromPacket(new NetworkPacket(PacketType.ConnectivityReport))!;

        Assert.Null(report.Best);
        Assert.Null(ConnectivityReport.FromPacket(ConnectivityReport.Request()));
    }

    [Theory]
    [InlineData(CellSignal.Nr, "5G")]
    [InlineData(CellSignal.Lte, "4G")]
    [InlineData(CellSignal.Umts, "3G")]
    [InlineData(CellSignal.Edge, "E")]
    [InlineData(CellSignal.Gsm, "2G")]
    [InlineData(CellSignal.Unknown, "")]
    public void NamesGenerations(string networkType, string generation) => Assert.Equal(generation, new CellSignal(networkType, 4).Generation);
}
