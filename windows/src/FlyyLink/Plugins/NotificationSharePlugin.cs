using FlyyLink.Core;
using FlyyLink.Model;
using FlyyLink.Shell;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace FlyyLink.Plugins;

/// Sends this PC's notifications to paired phones and computers ("kdeconnect.notification"), and removes one here
/// when it is dismissed there. `UserNotificationListener` needs package identity, so an unpackaged install reads
/// Windows' notification database instead, and can't remove notifications. Mirrors NotificationSharePlugin.swift.
internal sealed class NotificationSharePlugin(DeviceRegistry devices) : IPlugin
{
    private const string IdPrefix = "windows.";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly NotificationDatabase database = new();
    private readonly object gate = new();
    /// What Windows showed at the last look. Null until the first look, which sends nothing.
    private Dictionary<string, PhoneNotification>? current;
    private UserNotificationListener? listener;
    private bool readsDatabase;
    private bool enabled;

    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.NotificationRequest };

    public bool IsAvailable => listener is not null || readsDatabase;

    /// Off until turned on from the settings menu.
    public bool IsEnabled
    {
        get
        {
            lock (gate) return enabled;
        }
        set
        {
            lock (gate) enabled = value;
        }
    }

    /// Asks Windows for access, or else checks the database can be read. Call once, on the UI thread.
    public async Task StartAsync()
    {
        listener = await ListenerAsync();
        readsDatabase = listener is null && database.Read(1) is not null;
        if (!IsAvailable) return;

        _ = Task.Run(PollAsync);
    }

    public void OnPacket(NetworkPacket packet, Device device)
    {
        if (packet.GetBool("request") == true && IsEnabled && device.Link.Identity.DeviceType.IsComputer())
        {
            List<PhoneNotification> all;
            lock (gate) all = [.. (current ?? []).Values];

            all.ForEach(note => device.Send(note.ToPacket()));
        }

        if (packet.GetString("cancel") is string id && id.StartsWith(IdPrefix, StringComparison.Ordinal) && uint.TryParse(id[IdPrefix.Length..], out var number))
        {
            try
            {
                listener?.RemoveNotification(number);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Could not remove notification {number}: {e.Message}");
            }
        }
    }

    private static async Task<UserNotificationListener?> ListenerAsync()
    {
        try
        {
            var candidate = UserNotificationListener.Current;

            return await candidate.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed ? candidate : null;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"Notification access unavailable: {e.Message}");
            return null;
        }
    }

    /// Neither source has a change event for unpackaged apps, so compare what is showing every few seconds.
    private async Task PollAsync()
    {
        while (true)
        {
            try
            {
                if (await ShowingAsync() is { } showing) Update(showing);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Reading notifications failed: {e.Message}");
            }

            await Task.Delay(PollInterval);
        }
    }

    private async Task<List<PhoneNotification>?> ShowingAsync()
    {
        if (listener is not null) return (await listener.GetNotificationsAsync(NotificationKinds.Toast)).Select(Read).OfType<PhoneNotification>().ToList();

        return database.Read()?.Select(Read).OfType<PhoneNotification>().ToList();
    }

    private void Update(IReadOnlyList<PhoneNotification> showing)
    {
        List<PhoneNotification> added;
        List<string> removed;
        lock (gate)
        {
            var previous = current;
            current = showing.GroupBy(note => note.Id).ToDictionary(group => group.Key, group => group.First());
            if (previous is null || !enabled) return;

            added = showing.Where(note => !previous.ContainsKey(note.Id)).Reverse().ToList();
            removed = previous.Keys.Where(id => !current.ContainsKey(id)).ToList();
        }

        if (added.Count == 0 && removed.Count == 0) return;

        foreach (var device in devices.Paired.Where(Receives))
        {
            added.ForEach(note => device.Send(note.ToPacket()));
            removed.ForEach(id => device.Send(PhoneNotification.CancelPacket(id)));
        }
    }

    private static bool Receives(Device device)
    {
        var type = device.Link.Identity.DeviceType;

        return type.IsComputer() || type.IsPhone();
    }

    /// Our own toasts already came from another device, so they stay here.
    private static PhoneNotification? Read(UserNotification notification)
    {
        string appName;
        try
        {
            appName = notification.AppInfo.DisplayInfo.DisplayName;
        }
        catch (Exception)
        {
            appName = "Windows";
        }

        var texts = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements().Select(t => t.Text).ToList() ?? [];

        return Note(notification.Id, appName, texts, notification.CreationTime);
    }

    private static PhoneNotification? Read(NotificationDatabase.Toast toast)
    {
        if (toast.AppId.Contains("FlyyLink", StringComparison.OrdinalIgnoreCase)) return null;

        return Note(toast.Id, AppNames.Of(toast.AppId), toast.Texts, toast.Arrived);
    }

    private static PhoneNotification? Note(uint id, string appName, IReadOnlyList<string> texts, DateTimeOffset time)
    {
        if (appName == "FlyyLink" || texts.Count == 0) return null;

        return new PhoneNotification($"{IdPrefix}{id}", appName, texts[0], string.Join("\n", texts.Skip(1)), time.ToUnixTimeMilliseconds());
    }
}
