import Foundation

/// A person in the phone's contacts with the numbers they can be texted or called on. Mirrors PhoneContact in PhoneContacts.kt.
public struct PhoneContact: Equatable, Hashable, Sendable {
    public var name: String
    public var numbers: [String]

    public init(name: String, numbers: [String]) {
        self.name = name
        self.numbers = numbers
    }

    /// Whether `query` is part of the name, ignoring case, or its digits are part of a number.
    public func matches(_ query: String) -> Bool {
        let text = query.trimmingCharacters(in: .whitespaces)
        if text.isEmpty || name.localizedCaseInsensitiveContains(text) { return true }

        let digits = PhoneContacts.digits(text)
        return !digits.isEmpty && numbers.contains { PhoneContacts.digits($0).contains(digits) }
    }

    public init?(json: [String: JSONValue]) {
        let name = json["name"]?.stringValue?.trimmingCharacters(in: .whitespaces) ?? ""
        let numbers = (json["numbers"]?.arrayValue ?? [])
            .compactMap { $0.stringValue?.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
        guard !name.isEmpty, !numbers.isEmpty else { return nil }

        self.init(name: name, numbers: numbers)
    }

    public func toJSON() -> JSONValue {
        .object(["name": .string(name), "numbers": .array(numbers.map { .string($0) })])
    }
}

/// The phone's contacts for a computer's Messages window: it sends `contactsRequest`, the phone answers `contacts`.
/// Mirrors PhoneContacts.kt.
public enum PhoneContacts {
    public static func request() -> NetworkPacket { NetworkPacket(type: PacketType.contactsRequest) }

    public static func isRequest(_ packet: NetworkPacket) -> Bool { packet.type == PacketType.contactsRequest }

    public static func packet(_ contacts: [PhoneContact]) -> NetworkPacket {
        NetworkPacket(type: PacketType.contacts, body: ["contacts": .array(contacts.map { $0.toJSON() })])
    }

    public static func contacts(_ packet: NetworkPacket) -> [PhoneContact]? {
        guard packet.type == PacketType.contacts else { return nil }

        return (packet.body["contacts"]?.arrayValue ?? []).compactMap { $0.objectValue.flatMap(PhoneContact.init(json:)) }
    }

    /// The contacts that match `query`, those whose name starts with it first, then by name.
    public static func search(_ contacts: [PhoneContact], _ query: String) -> [PhoneContact] {
        let text = query.trimmingCharacters(in: .whitespaces)
        let starts = { (contact: PhoneContact) in contact.name.lowercased().hasPrefix(text.lowercased()) }

        return contacts.filter { $0.matches(text) }.sorted { a, b in
            if starts(a) != starts(b) { return starts(a) }
            return a.name.lowercased() < b.name.lowercased()
        }
    }

    public static func digits(_ text: String) -> String { String(text.filter(\.isASCII).filter(\.isNumber)) }
}
