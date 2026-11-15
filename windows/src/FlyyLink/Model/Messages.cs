using FlyyLink.Core;

namespace FlyyLink.Model;

/// One text conversation, as the Messages sidebar lists it.
public sealed record Conversation(long Id, IReadOnlyList<SmsAddress> Addresses, SmsMessage Latest)
{
    public string Title => Addresses.Count == 0 ? "Unknown" : string.Join(", ", Addresses.Select(a => a.Name ?? a.Address));

    public string Numbers => string.Join(", ", Addresses.Select(a => a.Address));

    public bool IsUnread => Latest.IsIncoming && !Latest.IsRead;

    public string Preview => (Latest.IsIncoming ? "" : "You: ") + (Latest.Body.Length > 0 ? Latest.Body : Latest.Parts.Count > 0 ? "Picture" : "");

    public string When => MessageTime.Short(Latest.Date);

    public bool Matches(string query) =>
        Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Numbers.Contains(query, StringComparison.Ordinal)
        || Latest.Body.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}

/// One bubble in a conversation. `Pending` texts were sent from this PC and the phone hasn't confirmed them yet.
public sealed record MessageBubble(long Key, string Body, bool IsMine, long Date, bool Pending = false, string? Picture = null)
{
    public bool HasBody => Body.Length > 0;

    public bool HasPicture => Picture is not null;

    public string Caption => Pending ? "Sending…" : MessageTime.Long(Date);
}

/// Message times the way a phone shows them.
public static class MessageTime
{
    public static DateTime Local(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;

    /// The time today, the weekday this week, otherwise the date.
    public static string Short(long milliseconds)
    {
        var date = Local(milliseconds);
        var today = DateTime.Today;

        if (date.Date == today) return date.ToShortTimeString();
        if (date.Date > today.AddDays(-7)) return date.ToString("ddd");

        return date.ToShortDateString();
    }

    public static string Long(long milliseconds)
    {
        var date = Local(milliseconds);

        return date.Date == DateTime.Today ? date.ToShortTimeString() : $"{date:ddd d MMM}, {date.ToShortTimeString()}";
    }
}

/// The phone's text conversations, for the Messages window. Use on the UI thread.
public sealed class MessagesStore : Observable
{
    private readonly Dictionary<long, List<SmsMessage>> threads = [];
    private readonly HashSet<long> requested = [];
    private readonly HashSet<long> loading = [];
    private readonly List<(long ThreadId, string Body, long Date, long Key)> pending = [];
    private IReadOnlyList<Conversation> conversations = [];
    private IReadOnlyList<PhoneContact> contacts = [];
    private string search = "";
    private long? selected;
    private bool hasPhone;
    private string? deviceId;
    private long nextPendingKey = -1;

    /// Sends a packet to the phone the messages come from. Set by the app model.
    internal Func<NetworkPacket, bool>? Send { get; set; }

    public IReadOnlyList<Conversation> Conversations
    {
        get => conversations;
        private set
        {
            if (!Set(ref conversations, value)) return;

            Raise(nameof(Shown));
            Raise(nameof(UnreadCount));
            Raise(nameof(IsEmpty));
            Raise(nameof(Selected));
        }
    }

    /// The conversations that match the search box.
    public IReadOnlyList<Conversation> Shown
    {
        get
        {
            var query = search.Trim();

            return query.Length == 0 ? conversations : conversations.Where(c => c.Matches(query)).ToList();
        }
    }

    public string Search
    {
        get => search;
        set
        {
            if (Set(ref search, value ?? "")) Raise(nameof(Shown));
        }
    }

    /// The phone's contacts, for picking who to text or call.
    public IReadOnlyList<PhoneContact> Contacts
    {
        get => contacts;
        private set => Set(ref contacts, value);
    }

    public bool HasPhone
    {
        get => hasPhone;
        private set
        {
            if (!Set(ref hasPhone, value)) return;

            Raise(nameof(EmptyText));
            Raise(nameof(IsLoadingList));
        }
    }

    public int UnreadCount => conversations.Count(c => c.IsUnread);

    public bool IsEmpty => conversations.Count == 0;

    public bool IsLoadingList => HasPhone && IsEmpty;

    public string EmptyText => HasPhone ? "Loading your texts…" : "Connect your phone to see texts";

    public Conversation? Selected => selected is long id ? conversations.FirstOrDefault(c => c.Id == id) : null;

    public bool HasSelection => Selected is not null;

    public bool IsLoadingThread => selected is long id && loading.Contains(id);

    /// The bubbles of the open conversation, oldest first.
    public IReadOnlyList<MessageBubble> Thread
    {
        get
        {
            if (selected is not long id) return [];

            var bubbles = threads.GetValueOrDefault(id, [])
                .Select(m => new MessageBubble(m.Id, m.Body, !m.IsIncoming, m.Date, Picture: m.Parts.FirstOrDefault(p => p.Thumbnail is not null)?.Thumbnail))
                .ToList();
            bubbles.AddRange(pending.Where(p => p.ThreadId == id).Select(p => new MessageBubble(p.Key, p.Body, true, p.Date, Pending: true)));

            return bubbles;
        }
    }

    /// A phone connected: start over with its conversations.
    public void Attach(string id)
    {
        if (deviceId != id)
        {
            deviceId = id;
            threads.Clear();
            requested.Clear();
            loading.Clear();
            pending.Clear();
            selected = null;
            Conversations = [];
            Contacts = [];
            RaiseThread();
        }

        HasPhone = true;
        Refresh();
    }

    public void Detach(string id)
    {
        if (deviceId == id) HasPhone = false;
    }

    public void Refresh()
    {
        Send?.Invoke(Sms.RequestConversations());
        Send?.Invoke(PhoneContacts.RequestPacket());
        foreach (var threadId in requested) Send?.Invoke(Sms.RequestConversation(threadId));
    }

    public void Ingest(IReadOnlyList<SmsMessage> messages, string fromDevice)
    {
        if (fromDevice != deviceId) return;

        if (messages.Count == 0) loading.Clear();

        var list = conversations.ToList();
        foreach (var message in messages)
        {
            Upsert(list, message);
            if (requested.Contains(message.ThreadId)) Insert(message);
            loading.Remove(message.ThreadId);
        }

        // A confirmed sent text replaces its pending copy.
        var sent = messages.Where(m => !m.IsIncoming).Select(m => m.Body).ToHashSet();
        pending.RemoveAll(p => sent.Contains(p.Body));

        Conversations = list.OrderByDescending(c => c.Latest.Date).ToList();
        RaiseThread();
    }

    public void Ingest(IReadOnlyList<PhoneContact> list, string fromDevice)
    {
        if (fromDevice == deviceId) Contacts = list;
    }

    public void Open(long threadId)
    {
        selected = threadId;

        if (requested.Add(threadId))
        {
            loading.Add(threadId);
            Send?.Invoke(Sms.RequestConversation(threadId));
        }

        Raise(nameof(Selected));
        RaiseThread();
    }

    public void LoadEarlier()
    {
        if (selected is not long id || threads.GetValueOrDefault(id) is not { Count: > 0 } thread) return;

        loading.Add(id);
        Send?.Invoke(Sms.RequestConversation(id, thread[0].Date));
        Raise(nameof(IsLoadingThread));
    }

    public void Reply(string text)
    {
        var body = text.Trim();
        if (Selected is not Conversation conversation || body.Length == 0) return;

        var subscription = threads.GetValueOrDefault(conversation.Id)?.LastOrDefault()?.SubscriptionId ?? conversation.Latest.SubscriptionId;
        if (Send?.Invoke(Sms.Send(conversation.Addresses.Select(a => a.Address), body, subscription)) != true) return;

        pending.Add((conversation.Id, body, DateTimeOffset.Now.ToUnixTimeMilliseconds(), nextPendingKey--));
        RaiseThread();
    }

    /// Starts a new conversation. Returns false when the phone isn't connected.
    public bool Compose(string number, string text)
    {
        var body = text.Trim();
        var address = number.Trim();
        if (body.Length == 0 || address.Length == 0) return false;

        return Send?.Invoke(Sms.Send([address], body)) == true;
    }

    private static void Upsert(List<Conversation> list, SmsMessage message)
    {
        var index = list.FindIndex(c => c.Id == message.ThreadId);
        if (index < 0)
        {
            list.Add(new Conversation(message.ThreadId, message.Addresses, message));
            return;
        }

        var conversation = list[index];
        if (message.Date >= conversation.Latest.Date) conversation = conversation with { Latest = message };
        if (conversation.Addresses.All(a => a.Name is null) && message.Addresses.Any(a => a.Name is not null)) conversation = conversation with { Addresses = message.Addresses };
        list[index] = conversation;
    }

    private void Insert(SmsMessage message)
    {
        if (!threads.TryGetValue(message.ThreadId, out var thread)) threads[message.ThreadId] = thread = [];

        var index = thread.FindIndex(m => m.Id == message.Id);
        if (index >= 0) thread[index] = message;
        else thread.Add(message);

        thread.Sort((a, b) => a.Date.CompareTo(b.Date));
    }

    private void RaiseThread()
    {
        Raise(nameof(Thread));
        Raise(nameof(HasSelection));
        Raise(nameof(IsLoadingThread));
    }
}
