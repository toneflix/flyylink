using System.Text.Json;

namespace FlyyLink.Model;

/// How the panel switches between several paired phones.
public enum PhoneSwitcherStyle
{
    /// The next phone's card peeks at the edge; clicking it moves on, round and round.
    Carousel,

    /// A list of devices on the device's name.
    Menu,
}

/// The user's choices and this computer's device id, kept in settings.json.
internal sealed class Settings
{
    private readonly string path;
    private readonly Values values;

    private sealed class Values
    {
        public string? DeviceId { get; set; }
        public bool MirrorsNotifications { get; set; } = true;
        public string? SelectedPhone { get; set; }
        public PhoneSwitcherStyle SwitcherStyle { get; set; } = PhoneSwitcherStyle.Carousel;
        public Dictionary<string, string> PhoneHotspots { get; set; } = new();
        public bool UsesShortcuts { get; set; } = true;
        public bool ShowsPhotosInExplorer { get; set; } = true;
        public bool MirrorMuted { get; set; }
        public bool SharesOwnNotifications { get; set; }
        public Dictionary<string, string> InputEdges { get; set; } = new();
        public bool AllowsRemoteInput { get; set; } = true;
        public bool RelaysPhones { get; set; } = true;
        public Core.FlyyDrop.Receiving FlyyDropReceiving { get; set; } = Core.FlyyDrop.Receiving.Off;
        public long FlyyDropUntil { get; set; }
    }

    public Settings(string path)
    {
        this.path = path;

        try
        {
            values = JsonSerializer.Deserialize<Values>(File.ReadAllText(path)) ?? new Values();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            values = new Values();
        }
    }

    /// Made once, on first launch.
    public string DeviceId
    {
        get
        {
            if (values.DeviceId is string id) return id;

            values.DeviceId = Core.DeviceIdentity.NewDeviceId();
            Save();

            return values.DeviceId;
        }
    }

    public bool MirrorsNotifications
    {
        get => values.MirrorsNotifications;
        set
        {
            values.MirrorsNotifications = value;
            Save();
        }
    }

    /// The phone the user picked when several are paired.
    public string? SelectedPhone
    {
        get => values.SelectedPhone;
        set
        {
            values.SelectedPhone = value;
            Save();
        }
    }

    public PhoneSwitcherStyle SwitcherStyle
    {
        get => values.SwitcherStyle;
        set
        {
            values.SwitcherStyle = value;
            Save();
        }
    }

    /// Whether the system-wide keyboard shortcuts are on.
    public bool UsesShortcuts
    {
        get => values.UsesShortcuts;
        set
        {
            values.UsesShortcuts = value;
            Save();
        }
    }

    /// Whether the phone's photos show as a folder in Explorer.
    public bool ShowsPhotosInExplorer
    {
        get => values.ShowsPhotosInExplorer;
        set
        {
            values.ShowsPhotosInExplorer = value;
            Save();
        }
    }

    /// Whether the phone's sound stays off while mirroring.
    public bool MirrorMuted
    {
        get => values.MirrorMuted;
        set
        {
            values.MirrorMuted = value;
            Save();
        }
    }

    /// Whether this PC's notifications go to paired computers.
    /// Pass phone notifications and calls between this PC and the user's other computers.
    public bool RelaysPhones
    {
        get => values.RelaysPhones;
        set
        {
            values.RelaysPhones = value;
            Save();
        }
    }

    public bool SharesNotifications
    {
        get => values.SharesOwnNotifications;
        set
        {
            values.SharesOwnNotifications = value;
            Save();
        }
    }

    /// The edge of this PC each paired computer sits beyond, by device id.
    public IReadOnlyDictionary<string, string> InputEdges => values.InputEdges;

    public void SetInputEdge(string deviceId, string? edge)
    {
        if (edge is null) values.InputEdges.Remove(deviceId);
        else values.InputEdges[deviceId] = edge;

        Save();
    }

    /// Whether paired computers may work this PC's keyboard and mouse.
    public bool AllowsRemoteInput
    {
        get => values.AllowsRemoteInput;
        set
        {
            values.AllowsRemoteInput = value;
            Save();
        }
    }

    /// Who may FlyyDrop to this PC without being paired, and when "Everyone for 10 minutes" ends.
    public (Core.FlyyDrop.Receiving Mode, long Until) FlyyDropReceiving
    {
        get => (values.FlyyDropReceiving, values.FlyyDropUntil);
        set
        {
            (values.FlyyDropReceiving, values.FlyyDropUntil) = value;
            Save();
        }
    }

    /// The hotspot name of each phone, by device id.
    public IReadOnlyDictionary<string, string> PhoneHotspots => values.PhoneHotspots;

    public void RememberHotspot(string deviceId, string ssid)
    {
        values.PhoneHotspots[deviceId] = ssid;
        Save();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(values));
        }
        catch (IOException)
        {
        }
    }
}
