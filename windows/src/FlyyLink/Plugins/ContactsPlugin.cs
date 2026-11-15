using FlyyLink.Core;

namespace FlyyLink.Plugins;

/// Hands the phone's contacts to the Messages window ("flyylink.contacts").
internal sealed class ContactsPlugin : IPlugin
{
    public IReadOnlySet<string> Incoming { get; } = new HashSet<string> { PacketType.Contacts };

    /// Called on a network thread with the contacts and the phone they came from.
    public Action<IReadOnlyList<PhoneContact>, string>? OnContacts { get; set; }

    public void OnPacket(NetworkPacket packet, Device device)
    {
        if (PhoneContacts.ContactsOf(packet) is { } contacts) OnContacts?.Invoke(contacts, device.Id);
    }
}
