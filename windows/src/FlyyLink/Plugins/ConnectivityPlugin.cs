using FlyyLink.Core;

namespace FlyyLink.Plugins;

/// Keeps each phone's mobile signal for the panel.
internal sealed class ConnectivityPlugin : IPlugin
{
    private readonly Dictionary<string, ConnectivityReport> reports = [];
    private readonly object gate = new();

    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.ConnectivityReport };

    /// Called on a network thread when a phone's signal changes.
    public Action? OnChange { get; set; }

    public CellSignal? Signal(string deviceId)
    {
        lock (gate) return reports.GetValueOrDefault(deviceId)?.Best;
    }

    public void OnPaired(Device device) => device.Send(ConnectivityReport.Request());

    public void OnPacket(NetworkPacket packet, Device device)
    {
        if (ConnectivityReport.FromPacket(packet) is not ConnectivityReport report) return;

        bool changed;
        lock (gate)
        {
            changed = !reports.TryGetValue(device.Id, out var known) || !known.Matches(report);
            reports[device.Id] = report;
        }

        if (changed) OnChange?.Invoke();
    }
}
