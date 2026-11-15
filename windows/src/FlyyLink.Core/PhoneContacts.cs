using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// A person in the phone's contacts with the numbers they can be texted or called on.
/// Mirrors PhoneContact in PhoneContacts.kt and PhoneContacts.swift.
public sealed record PhoneContact(string Name, IReadOnlyList<string> Numbers)
{
    /// Whether `query` is part of the name, ignoring case, or its digits are part of a number.
    public bool Matches(string query)
    {
        var text = query.Trim();
        if (text.Length == 0 || Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)) return true;

        var digits = PhoneContacts.Digits(text);
        return digits.Length > 0 && Numbers.Any(number => PhoneContacts.Digits(number).Contains(digits, StringComparison.Ordinal));
    }

    public bool Equals(PhoneContact? other) => other is not null && Name == other.Name && Numbers.SequenceEqual(other.Numbers);

    public override int GetHashCode() => HashCode.Combine(Name, Numbers.Count);

    public static PhoneContact? FromJson(JsonObject json)
    {
        var name = json["name"].AsString()?.Trim() ?? "";
        var numbers = (json["numbers"] as JsonArray ?? [])
            .Select(node => node.AsString()?.Trim())
            .OfType<string>()
            .Where(number => number.Length > 0)
            .ToList();
        if (name.Length == 0 || numbers.Count == 0) return null;

        return new PhoneContact(name, numbers);
    }

    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["numbers"] = new JsonArray(Numbers.Select(number => (JsonNode?)number).ToArray()),
    };
}

/// The phone's contacts for a computer's Messages window: it sends "flyylink.contacts.request", the phone answers
/// "flyylink.contacts". Mirrors PhoneContacts.kt and PhoneContacts.swift.
public static class PhoneContacts
{
    public static NetworkPacket RequestPacket() => new(PacketType.ContactsRequest, new JsonObject());

    public static bool IsRequest(NetworkPacket packet) => packet.Type == PacketType.ContactsRequest;

    public static NetworkPacket Packet(IEnumerable<PhoneContact> contacts) => new(PacketType.Contacts, new JsonObject
    {
        ["contacts"] = new JsonArray(contacts.Select(contact => (JsonNode?)contact.ToJson()).ToArray()),
    });

    public static IReadOnlyList<PhoneContact>? ContactsOf(NetworkPacket packet)
    {
        if (packet.Type != PacketType.Contacts) return null;

        return (packet.Body["contacts"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(PhoneContact.FromJson)
            .OfType<PhoneContact>()
            .ToList();
    }

    /// The contacts that match `query`, those whose name starts with it first, then by name.
    public static IReadOnlyList<PhoneContact> Search(IEnumerable<PhoneContact> contacts, string query)
    {
        var text = query.Trim();

        return contacts.Where(contact => contact.Matches(text))
            .OrderBy(contact => !contact.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(contact => contact.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
    }

    public static string Digits(string text) => new(text.Where(char.IsAsciiDigit).ToArray());
}
