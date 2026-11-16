namespace FlyyLink.Core;

/// A feature that handles some packet types for paired devices. Mirrors Plugin.kt and Plugin.swift.
/// Methods may be called from a background thread.
public interface IPlugin
{
    /// Packet types this plugin accepts.
    IReadOnlySet<string> Incoming { get; }

    /// The device just became paired, or connected while already paired.
    void OnPaired(Device device) { }

    /// The device turned a feature on or off; see `PhoneFeatures`.
    void OnFeaturesChanged(Device device) { }

    void OnPacket(NetworkPacket packet, Device device);
}

/// Hands each packet to the plugins that accept its type.
public sealed class PluginRouter(IReadOnlyList<IPlugin> plugins)
{
    public IReadOnlySet<string> IncomingCapabilities => plugins.SelectMany(p => p.Incoming).ToHashSet();

    public void OnPaired(Device device)
    {
        foreach (var plugin in plugins) plugin.OnPaired(device);
    }

    public void OnFeaturesChanged(Device device)
    {
        foreach (var plugin in plugins) plugin.OnFeaturesChanged(device);
    }

    /// Returns false when no plugin accepts the packet.
    public bool Dispatch(NetworkPacket packet, Device device)
    {
        var targets = plugins.Where(p => p.Incoming.Contains(packet.Type)).ToList();
        foreach (var plugin in targets) plugin.OnPacket(packet, device);

        return targets.Count > 0;
    }
}
