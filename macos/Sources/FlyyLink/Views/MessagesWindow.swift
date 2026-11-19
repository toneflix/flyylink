import AppKit
import SwiftUI
import FlyyLinkKit

/// The phone's text conversations, in their own window.
struct MessagesWindow: View {
    @ObservedObject var store: MessagesStore
    @ObservedObject var model: AppModel

    @State private var search = ""
    @State private var composing = false

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationSplitViewColumnWidth(min: 240, ideal: 280, max: 360)
        } detail: {
            if let conversation = store.selectedConversation {
                ConversationView(conversation: conversation, store: store, model: model)
                    .id(conversation.id)
            } else {
                EmptyDetail(hasPhone: store.hasPhone)
            }
        }
        .frame(minWidth: 760, minHeight: 500)
        .toolbar {
            ToolbarItem(placement: .navigation) {
                PhonePicker(model: model)
            }
            ToolbarItemGroup {
                Button { store.showsDialer = true } label: { Label("Call", lucide: .phone) }
                    .help("Call a number from your phone")
                    .disabled(!store.hasPhone)
                Button { composing = true } label: { Label("New message", lucide: .squarePen) }
                    .help("Send a new text")
                    .disabled(!store.hasPhone)
            }
        }
        .sheet(isPresented: $composing) { NewMessageSheet(store: store) }
        .sheet(isPresented: $store.showsDialer) { DialSheet(model: model, store: store) }
        .onAppear { if store.conversations.isEmpty { store.refresh() } }
    }

    private var sidebar: some View {
        List(selection: Binding(get: { store.selected }, set: { if let id = $0 { store.open(id) } })) {
            ForEach(filtered) { conversation in
                ConversationRow(conversation: conversation)
                    .tag(conversation.id)
            }
        }
        .searchable(text: $search, placement: .sidebar, prompt: "Search")
        .overlay {
            if store.conversations.isEmpty {
                VStack(spacing: 8) {
                    if store.hasPhone { ProgressView().controlSize(.small) }
                    Text(store.hasPhone ? "Loading your texts…" : "Connect your phone to see texts")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    private var filtered: [Conversation] {
        let query = search.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty else { return store.conversations }

        return store.conversations.filter {
            $0.title.localizedCaseInsensitiveContains(query)
                || $0.numbers.contains(query)
                || $0.latest.body.localizedCaseInsensitiveContains(query)
        }
    }
}

private struct ConversationRow: View {
    let conversation: Conversation

    var body: some View {
        HStack(spacing: 10) {
            Avatar(name: conversation.title, size: 36)

            VStack(alignment: .leading, spacing: 2) {
                HStack {
                    Text(conversation.title)
                        .font(.system(size: 13, weight: conversation.isUnread ? .bold : .semibold))
                        .lineLimit(1)
                    Spacer(minLength: 4)
                    Text(MessageTime.short(conversation.latest.date))
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                }
                HStack(alignment: .top) {
                    Text(snippet)
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                    Spacer(minLength: 4)
                    if conversation.isUnread {
                        Circle().fill(Theme.accent).frame(width: 8, height: 8).padding(.top, 4)
                    }
                }
            }
        }
        .padding(.vertical, 4)
    }

    private var snippet: String {
        let latest = conversation.latest
        let body = latest.body.isEmpty && !latest.attachments.isEmpty ? "Photo" : latest.body
        return latest.isIncoming ? body : "You: \(body)"
    }
}

private struct EmptyDetail: View {
    let hasPhone: Bool

    var body: some View {
        VStack(spacing: 12) {
            Image(lucide: .messagesSquare, size: 40, weight: .light)
                .foregroundStyle(Theme.accent)
            Text(hasPhone ? "Choose a conversation" : "Your phone isn't connected")
                .font(.system(size: 17, weight: .semibold))
            if !hasPhone {
                Text("Open FlyyLink on your phone and make sure it's on the same Wi-Fi.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

/// One conversation: bubbles, and a field to reply.
private struct ConversationView: View {
    let conversation: Conversation
    @ObservedObject var store: MessagesStore
    @ObservedObject var model: AppModel

    @State private var draft = ""
    @FocusState private var focused: Bool

    private var messages: [SmsMessage] { store.threads[conversation.id] ?? [] }

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()

            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 4) {
                        if !messages.isEmpty {
                            Button(store.loading.contains(conversation.id) ? "Loading…" : "Load earlier messages") {
                                store.loadEarlier(conversation.id)
                            }
                            .buttonStyle(.plain)
                            .font(.system(size: 12, weight: .medium))
                            .foregroundStyle(Theme.accent)
                            .padding(.vertical, 8)
                        }

                        ForEach(Array(messages.enumerated()), id: \.element.id) { index, message in
                            if index == 0 || MessageTime.isNewDay(message.date, after: messages[index - 1].date) {
                                Text(MessageTime.day(message.date))
                                    .font(.system(size: 11, weight: .medium))
                                    .foregroundStyle(.secondary)
                                    .padding(.vertical, 8)
                            }
                            Bubble(message: message)
                        }

                        ForEach(store.pending(in: conversation.id)) { text in
                            PendingBubble(text: text)
                        }

                        Color.clear.frame(height: 1).id("bottom")
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 10)
                }
                .overlay {
                    if messages.isEmpty && store.loading.contains(conversation.id) { ProgressView() }
                }
                .onAppear { proxy.scrollTo("bottom") }
                .onChange(of: messages.last?.id) { _ in withAnimation { proxy.scrollTo("bottom") } }
                .onChange(of: store.pending.count) { _ in withAnimation { proxy.scrollTo("bottom") } }
            }

            Divider()
            composer
        }
        .onAppear { focused = true }
    }

    private var header: some View {
        HStack(spacing: 10) {
            Avatar(name: conversation.title, size: 32)
            VStack(alignment: .leading, spacing: 1) {
                Text(conversation.title).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                if conversation.title != conversation.numbers {
                    Text(conversation.numbers).font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer()
            if let number = conversation.addresses.first?.address, conversation.addresses.count == 1 {
                Button { model.dial(number, name: conversation.addresses.first?.name) } label: {
                    Image(lucide: .phone, size: 13, weight: .semibold)
                        .foregroundStyle(Theme.accent)
                        .frame(width: 32, height: 32)
                        .background(Theme.tint, in: Circle())
                }
                .buttonStyle(PressStyle())
                .help("Call from your phone")
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
    }

    private var composer: some View {
        HStack(alignment: .bottom, spacing: 8) {
            TextField("Text message", text: $draft, axis: .vertical)
                .textFieldStyle(.plain)
                .font(.system(size: 13))
                .lineLimit(1...6)
                .focused($focused)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .background(Theme.card, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).stroke(Theme.border))
                .onSubmit(send)

            Button(action: send) {
                Image(lucide: .arrowUp, size: 13, weight: .bold)
                    .foregroundStyle(Theme.onAccent)
                    .frame(width: 32, height: 32)
                    .background(Circle().fill(canSend ? Theme.accent : Color.secondary.opacity(0.4)))
            }
            .buttonStyle(PressStyle())
            .disabled(!canSend)
            .keyboardShortcut(.return, modifiers: .command)
        }
        .padding(12)
    }

    private var canSend: Bool { store.hasPhone && !draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    private func send() {
        guard canSend else { return }

        store.reply(in: conversation, draft)
        draft = ""
    }
}

private struct Bubble: View {
    let message: SmsMessage

    var body: some View {
        HStack {
            if !message.isIncoming { Spacer(minLength: 80) }

            VStack(alignment: message.isIncoming ? .leading : .trailing, spacing: 4) {
                ForEach(message.attachments, id: \.partId) { attachment in
                    if let image = attachment.thumbnail.flatMap({ Data(base64Encoded: $0) }).flatMap(NSImage.init(data:)) {
                        Image(nsImage: image)
                            .resizable()
                            .aspectRatio(contentMode: .fit)
                            .frame(maxWidth: 220, maxHeight: 220)
                            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                    }
                }

                if !message.body.isEmpty {
                    Text(message.body)
                        .font(.system(size: 13))
                        .textSelection(.enabled)
                        .foregroundStyle(message.isIncoming ? Color.primary : Theme.onAccent)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 8)
                        .background(
                            RoundedRectangle(cornerRadius: 17, style: .continuous)
                                .fill(message.isIncoming ? Theme.thumb : Theme.accent)
                        )
                }
            }
            .help(MessageTime.full(message.date))

            if message.isIncoming { Spacer(minLength: 80) }
        }
    }
}

private struct PendingBubble: View {
    let text: PendingText

    var body: some View {
        HStack {
            Spacer(minLength: 80)
            VStack(alignment: .trailing, spacing: 2) {
                Text(text.body)
                    .font(.system(size: 13))
                    .foregroundStyle(Theme.onAccent)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
                    .background(RoundedRectangle(cornerRadius: 17, style: .continuous).fill(Theme.accent.opacity(0.6)))
                Text("Sending…").font(.system(size: 10)).foregroundStyle(.secondary)
            }
        }
    }
}

/// A colored circle with initials, the same color each time for the same name.
struct Avatar: View {
    let name: String
    var image: NSImage?
    var size: CGFloat = 36

    var body: some View {
        Group {
            if let image {
                Image(nsImage: image).resizable().aspectRatio(contentMode: .fill)
            } else {
                Circle()
                    .fill(color.gradient)
                    .overlay(
                        Text(initials)
                            .font(.system(size: size * 0.38, weight: .semibold))
                            .foregroundStyle(.white)
                    )
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
    }

    private var initials: String {
        let words = name.split(separator: " ").filter { $0.first?.isLetter == true }
        guard !words.isEmpty else { return "#" }

        return words.prefix(2).compactMap { $0.first.map(String.init) }.joined().uppercased()
    }

    private var color: Color {
        let palette: [Color] = [.blue, .purple, .pink, .orange, .teal, .indigo, .green, .red]
        let hash = name.unicodeScalars.reduce(0) { ($0 &* 31 &+ Int($1.value)) & 0x7fffffff }
        return palette[hash % palette.count]
    }
}

private struct NewMessageSheet: View {
    @ObservedObject var store: MessagesStore
    @Environment(\.dismiss) private var dismiss

    @State private var number = ""
    @State private var text = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("New message").font(.system(size: 16, weight: .bold))
            ContactField(contacts: store.contacts, number: $number)
            TextField("Message", text: $text, axis: .vertical)
                .textFieldStyle(.roundedBorder)
                .lineLimit(3...6)
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Send") {
                    store.compose(to: number, text)
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(number.trimmingCharacters(in: .whitespaces).isEmpty || text.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20)
        .frame(width: 360)
    }
}

private struct DialSheet: View {
    @ObservedObject var model: AppModel
    @ObservedObject var store: MessagesStore
    @Environment(\.dismiss) private var dismiss

    @State private var number = ""
    @State private var name: String?

    var body: some View {
        VStack(spacing: 14) {
            Text("Call from your phone").font(.system(size: 16, weight: .bold))
            ContactField(contacts: store.contacts, number: $number, name: $name, onSubmit: call)
                .font(.system(size: 18, weight: .medium, design: .rounded))
            HStack {
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Spacer()
                Button(action: call) { Label("Call", lucide: .phone) }
                    .keyboardShortcut(.defaultAction)
                    .disabled(number.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20)
        .frame(width: 320)
    }

    private func call() {
        guard !number.trimmingCharacters(in: .whitespaces).isEmpty else { return }

        model.dial(number, name: name)
        dismiss()
    }
}

/// A number field that suggests the phone's contacts as you type a name or number.
private struct ContactField: View {
    let contacts: [PhoneContact]
    @Binding var number: String
    var name: Binding<String?> = .constant(nil)
    var onSubmit: () -> Void = {}

    @State private var picked = ""

    private struct Suggestion: Identifiable {
        let name: String
        let number: String
        var id: String { name + number }
    }

    private var suggestions: [Suggestion] {
        let query = number.trimmingCharacters(in: .whitespaces)
        guard !query.isEmpty, query != picked else { return [] }

        let matches = PhoneContacts.search(contacts, query).flatMap { contact in
            contact.numbers.map { Suggestion(name: contact.name, number: $0) }
        }
        return Array(matches.prefix(5))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            TextField(contacts.isEmpty ? "Phone number" : "Name or number", text: $number)
                .textFieldStyle(.roundedBorder)
                .onSubmit(onSubmit)
                .onChange(of: number) { if $0 != picked { name.wrappedValue = nil } }

            if let contact = name.wrappedValue {
                Text(contact)
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
            }

            ForEach(suggestions) { suggestion in
                Button { pick(suggestion) } label: {
                    HStack {
                        Text(suggestion.name).font(.system(size: 13, weight: .medium))
                        Spacer()
                        Text(suggestion.number)
                            .font(.system(size: 12))
                            .foregroundStyle(.secondary)
                    }
                    .contentShape(Rectangle())
                    .padding(.vertical, 3)
                    .padding(.horizontal, 6)
                }
                .buttonStyle(.plain)
            }
        }
    }

    private func pick(_ suggestion: Suggestion) {
        picked = suggestion.number
        number = suggestion.number
        name.wrappedValue = suggestion.name
    }
}

/// Times as Messages shows them.
enum MessageTime {
    private static let calendar = Calendar.current

    static func date(_ millis: Int64) -> Date { Date(timeIntervalSince1970: TimeInterval(millis) / 1000) }

    static func short(_ millis: Int64) -> String {
        let date = date(millis)
        if calendar.isDateInToday(date) { return date.formatted(date: .omitted, time: .shortened) }
        if calendar.isDateInYesterday(date) { return "Yesterday" }

        return date.formatted(.dateTime.day().month(.abbreviated))
    }

    static func day(_ millis: Int64) -> String {
        let date = date(millis)
        if calendar.isDateInToday(date) { return "Today" }
        if calendar.isDateInYesterday(date) { return "Yesterday" }

        return date.formatted(date: .complete, time: .omitted)
    }

    static func full(_ millis: Int64) -> String {
        date(millis).formatted(date: .abbreviated, time: .shortened)
    }

    static func isNewDay(_ millis: Int64, after previous: Int64) -> Bool {
        !calendar.isDate(date(millis), inSameDayAs: date(previous))
    }
}
