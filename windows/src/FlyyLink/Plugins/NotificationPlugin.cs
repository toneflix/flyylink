using System.Security.Cryptography;
using System.Text;
using FlyyLink.Core;
using FlyyLink.Model;
using Microsoft.Windows.AppNotifications.Builder;

namespace FlyyLink.Plugins;

/// Shows phone notifications as Windows notifications. Replying, dismissing or pressing
/// one of the phone's buttons there is sent back to the phone.
internal sealed class NotificationPlugin(ToastCenter toasts, DeviceRegistry devices) : IPlugin
{
    private const string ReplyInput = "reply";
    private const string ActionPrefix = "action.";

    /// Windows shows at most five buttons on a notification.
    private const int MaxButtons = 5;

    private readonly NotificationIcons icons = new();
    private readonly Dictionary<string, string> shown = [];
    private readonly object gate = new();
    private bool enabled = true;

    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.Notification };

    /// Paused from the panel: phone notifications are dropped instead of shown.
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

    /// Called after a phone notification is shown, on any thread.
    public Action? OnShown { get; set; }

    /// Called on any thread with each new phone notification and the phone's name, before it shows.
    public Action<PhoneNotification, string>? OnArrived { get; set; }

    /// Sends replies, buttons and dismissals to a phone. Set by the app model, so they also reach phones
    /// that another computer passed on.
    public Func<string, NetworkPacket, bool> Send { get; set; } = (id, packet) => devices[id]?.Send(packet) == true;

    public void OnPaired(Device device) => device.Send(NotificationRequests.RequestAll());

    /// Fills in what is showing on the phone once its notifications are turned on.
    public void OnFeaturesChanged(Device device) => OnPaired(device);

    public void OnPacket(NetworkPacket packet, Device device) => Present(packet, device.Id, device.Name, device);

    /// A notification another computer passed on from its phone. Relayed notifications come without icons.
    public void PresentRelayed(NetworkPacket packet, string phoneId, string phoneName) => Present(packet, phoneId, phoneName, null);

    private void Present(NetworkPacket packet, string deviceId, string deviceName, Device? iconsFrom)
    {
        if (PhoneNotification.CancelledId(packet) is string cancelled)
        {
            var tag = Tag(cancelled);
            lock (gate) shown.Remove(Identifier(deviceId, cancelled));
            toasts.Remove(tag, deviceId);
            return;
        }

        if (!IsEnabled || PhoneNotification.FromPacket(packet) is not PhoneNotification note) return;

        // A resend after reconnecting, or an update that changes nothing, should not pop up again.
        var summary = $"{note.Title}\n{note.Text}";
        lock (gate)
        {
            var identifier = Identifier(deviceId, note.Id);
            if (shown.TryGetValue(identifier, out var previous) && previous == summary) return;

            shown[identifier] = summary;
        }

        OnArrived?.Invoke(note, deviceName);
        _ = ShowAsync(note, packet, deviceId, deviceName, iconsFrom);
    }

    private async Task ShowAsync(PhoneNotification note, NetworkPacket packet, string deviceId, string deviceName, Device? iconsFrom)
    {
        // The phone sends the app icon, or the sender's photo, as the payload.
        var icon = iconsFrom is null ? null : await icons.IconAsync(packet, note.IconHash, iconsFrom);

        toasts.Show(key =>
        {
            var builder = new AppNotificationBuilder()
                .AddText(note.Title.Length == 0 ? note.AppName : note.Title)
                .AddText(note.Text)
                .SetAttributionText(note.Title.Length == 0 ? deviceName : $"{note.AppName} · {deviceName}");

            if (icon is not null) builder.SetAppLogoOverride(new Uri(icon), AppNotificationImageCrop.Circle);

            var buttons = 0;
            if (note.ReplyId is not null)
            {
                builder.AddTextBox(ReplyInput, "Message", "");
                builder.AddButton(ToastCenter.Button("Send", "reply", key).SetInputId(ReplyInput));
                buttons++;
            }

            // The phone's own buttons, such as "Mark as read".
            foreach (var title in note.ActionTitles.Take(MaxButtons - buttons - (note.IsClearable ? 1 : 0)))
            {
                builder.AddButton(ToastCenter.Button(title, ActionPrefix + title, key));
                buttons++;
            }

            if (note.IsClearable && buttons < MaxButtons) builder.AddButton(ToastCenter.Button("Dismiss on phone", "dismiss", key));

            return builder;
        }, (action, input) => Respond(action, input, deviceId, note), Tag(note.Id), deviceId, quiet: note.Silent);

        if (!note.Silent) OnShown?.Invoke();
    }

    private void Respond(string action, IDictionary<string, string> input, string deviceId, PhoneNotification note)
    {
        switch (action)
        {
            case "reply" when note.ReplyId is not null && input.TryGetValue(ReplyInput, out var text) && text.Trim().Length > 0:
                Send(deviceId, NotificationRequests.Reply(note.ReplyId, text));
                break;
            case "dismiss":
                Send(deviceId, NotificationRequests.Dismiss(note.Id));
                break;
            case var pressed when pressed.StartsWith(ActionPrefix, StringComparison.Ordinal):
                Send(deviceId, NotificationRequests.Action(note.Id, pressed[ActionPrefix.Length..]));
                break;
        }
    }

    private static string Identifier(string device, string id) => $"{device}/{id}";

    /// Windows allows short tags only, so the phone's id is hashed.
    private static string Tag(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..32];
}
