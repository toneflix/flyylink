import Foundation
import FlyyLinkKit

/// One text conversation, as the sidebar lists it.
struct Conversation: Identifiable, Equatable {
    let id: Int64
    var addresses: [SmsAddress]
    var latest: SmsMessage

    var title: String {
        let names = addresses.map { $0.name ?? $0.address }
        return names.isEmpty ? "Unknown" : names.joined(separator: ", ")
    }

    var numbers: String { addresses.map(\.address).joined(separator: ", ") }

    var isUnread: Bool { latest.isIncoming && !latest.isRead }
}

/// A text sent from the Mac that the phone hasn't confirmed yet.
struct PendingText: Identifiable, Equatable {
    let id = UUID()
    let threadId: Int64?
    let body: String
    let date = Date()
}

/// The phone's text conversations, kept on the main thread for the Messages window.
@MainActor
final class MessagesStore: ObservableObject {
    @Published private(set) var conversations: [Conversation] = []
    @Published private(set) var threads: [Int64: [SmsMessage]] = [:]
    @Published private(set) var pending: [PendingText] = []
    @Published private(set) var loading: Set<Int64> = []
    @Published private(set) var hasPhone = false
    @Published private(set) var contacts: [PhoneContact] = []
    @Published var selected: Int64?
    @Published var showsDialer = false

    /// Sends a packet to the phone the messages come from. Set by the app model.
    var send: ((NetworkPacket) -> Bool)?

    private var deviceId: String?
    private var requested: Set<Int64> = []

    var unreadCount: Int { conversations.filter(\.isUnread).count }

    var selectedConversation: Conversation? { conversations.first { $0.id == selected } }

    /// A phone connected: start over with its conversations.
    func attach(_ deviceId: String) {
        if self.deviceId != deviceId {
            self.deviceId = deviceId
            conversations = []
            threads = [:]
            requested = []
            selected = nil
            contacts = []
        }

        hasPhone = true
        refresh()
    }

    func detach(_ deviceId: String) {
        guard self.deviceId == deviceId else { return }
        hasPhone = false
    }

    func refresh() {
        _ = send?(Sms.requestConversations())
        _ = send?(PhoneContacts.request())
        requested.forEach { _ = send?(Sms.requestConversation($0)) }
    }

    func ingest(_ messages: [SmsMessage], from deviceId: String) {
        guard deviceId == self.deviceId else { return }
        if messages.isEmpty { loading.removeAll() }

        for message in messages {
            upsertConversation(message)
            if requested.contains(message.threadId) { insert(message) }
            loading.remove(message.threadId)
        }

        // A confirmed sent text replaces its pending copy.
        let sent = messages.filter { !$0.isIncoming }
        pending.removeAll { text in sent.contains { $0.body == text.body } }

        conversations.sort { $0.latest.date > $1.latest.date }
    }

    func ingest(_ contacts: [PhoneContact], from deviceId: String) {
        guard deviceId == self.deviceId else { return }

        self.contacts = contacts
    }

    func open(_ threadId: Int64) {
        selected = threadId
        guard !requested.contains(threadId) else { return }

        requested.insert(threadId)
        loading.insert(threadId)
        _ = send?(Sms.requestConversation(threadId))
    }

    func loadEarlier(_ threadId: Int64) {
        guard let oldest = threads[threadId]?.first?.date else { return }

        loading.insert(threadId)
        _ = send?(Sms.requestConversation(threadId, before: oldest))
    }

    func reply(in conversation: Conversation, _ text: String) {
        let body = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !body.isEmpty else { return }

        let subscription = threads[conversation.id]?.last?.subscriptionId ?? conversation.latest.subscriptionId
        if send?(Sms.send(to: conversation.addresses.map(\.address), body: body, subscriptionId: subscription)) == true {
            pending.append(PendingText(threadId: conversation.id, body: body))
        }
    }

    /// Starts a new conversation. Returns false when the phone isn't connected.
    @discardableResult
    func compose(to number: String, _ text: String) -> Bool {
        let body = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let address = number.trimmingCharacters(in: .whitespaces)
        guard !body.isEmpty, !address.isEmpty else { return false }

        return send?(Sms.send(to: [address], body: body)) == true
    }

    func pending(in threadId: Int64) -> [PendingText] {
        pending.filter { $0.threadId == threadId }
    }

    private func upsertConversation(_ message: SmsMessage) {
        guard let index = conversations.firstIndex(where: { $0.id == message.threadId }) else {
            conversations.append(Conversation(id: message.threadId, addresses: message.addresses, latest: message))
            return
        }

        if message.date >= conversations[index].latest.date {
            conversations[index].latest = message
        }
        if conversations[index].addresses.allSatisfy({ $0.name == nil }), message.addresses.contains(where: { $0.name != nil }) {
            conversations[index].addresses = message.addresses
        }
    }

    private func insert(_ message: SmsMessage) {
        var thread = threads[message.threadId, default: []]
        if let index = thread.firstIndex(where: { $0.id == message.id }) {
            thread[index] = message
        } else {
            thread.append(message)
        }

        thread.sort { $0.date < $1.date }
        threads[message.threadId] = thread
    }
}

/// Hands text packets from the phone to the Messages window.
final class SmsPlugin: Plugin {
    let incoming: Set<String> = [PacketType.smsMessages]

    /// Called on any thread with the messages and the device they came from.
    var onMessages: (([SmsMessage], String) -> Void)?

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        guard let messages = Sms.messages(packet) else { return }

        onMessages?(messages, device.id)
    }
}

/// Hands the phone's contacts to the Messages window.
final class ContactsPlugin: Plugin {
    let incoming: Set<String> = [PacketType.contacts]

    /// Called on any thread with the contacts and the device they came from.
    var onContacts: (([PhoneContact], String) -> Void)?

    func onPacket(_ packet: NetworkPacket, from device: Device) {
        guard let contacts = PhoneContacts.contacts(packet) else { return }

        onContacts?(contacts, device.id)
    }
}
