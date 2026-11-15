using FlyyLink.Core;
using FlyyLink.Model;
using FlyyLink.Shell;

namespace FlyyLink.Plugins;

/// Shares this PC's keyboard and mouse with paired computers, and lets them and paired phones work it ("flyylink.input").
internal sealed class InputSharePlugin : IPlugin
{
    private readonly DeviceRegistry devices;
    private readonly Settings settings;

    public InputSharePlugin(DeviceRegistry devices, Settings settings)
    {
        this.devices = devices;
        this.settings = settings;

        bool Send(string id, RemoteInput input) => devices[id] is { IsPaired: true } device && device.Send(input.ToPacket());
        Capture.Send = Send;
        Injector.Send = Send;
        Capture.Edges = () => Edges.Where(pair => devices[pair.Key]?.IsPaired == true).ToDictionary(pair => pair.Key, pair => pair.Value);
        Capture.IsControlled = () => Injector.ControllerId is not null;
        Capture.OnChange = _ => OnChange?.Invoke();
        Injector.OnChange = _ => OnChange?.Invoke();

        if (Edges.Count > 0) Capture.Start();
    }

    public InputCapture Capture { get; } = new();

    public InputInjector Injector { get; } = new();

    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.Input };

    /// Called on any thread when the edges, or who is working which computer, change.
    public Action? OnChange { get; set; }

    /// Whether paired computers and phones may work this PC.
    public bool AllowsControl
    {
        get => settings.AllowsRemoteInput;
        set
        {
            settings.AllowsRemoteInput = value;
            if (!value && Injector.ControllerId is string id) Injector.Lost(id);
            OnChange?.Invoke();
        }
    }

    public ScreenEdge? Edge(string deviceId) => Edges.TryGetValue(deviceId, out var edge) ? edge : null;

    /// Puts a computer beyond one edge of this PC, or null to stop sharing with it.
    public void SetEdge(ScreenEdge? edge, string deviceId)
    {
        settings.SetInputEdge(deviceId, edge?.WireName());

        if (Edges.Count == 0) Capture.Stop();
        else Capture.Start();

        OnChange?.Invoke();
    }

    public void OnPacket(NetworkPacket packet, Device device)
    {
        if (RemoteInput.FromPacket(packet) is not RemoteInput input) return;

        if (input is RemoteInput.Leave leave)
        {
            Capture.Resume(device.Id, leave.Edge, leave.Position);
            return;
        }

        if (input is RemoteInput.Begin) device.Send(new RemoteInput.Begin(RemoteInput.Windows, AllowsControl).ToPacket());
        if (!AllowsControl) return;

        if (input is RemoteInput.Enter enter) Learn(enter.Edge, device.Id);
        Injector.Handle(input, device.Id);
    }

    public void Forget(string deviceId)
    {
        Capture.Lost(deviceId);
        Injector.Lost(deviceId);
    }

    private IReadOnlyDictionary<string, ScreenEdge> Edges => settings.InputEdges
        .Where(pair => ScreenEdges.Parse(pair.Value) is not null)
        .ToDictionary(pair => pair.Key, pair => ScreenEdges.Parse(pair.Value)!.Value);

    /// A computer that came in through an edge sits beyond it, so the pointer can go back the same way.
    private void Learn(ScreenEdge edge, string deviceId)
    {
        var edges = Edges;
        if (edges.ContainsKey(deviceId) || edges.Values.Contains(edge)) return;

        SetEdge(edge, deviceId);
    }
}
