using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// A file or folder in the phone's storage. `Path` starts at the storage root, like "/DCIM/Camera/IMG_1.jpg"; `Modified` is in milliseconds.
/// Mirrors PhoneFile in PhoneFiles.kt and PhoneFiles.swift.
public sealed record PhoneFile(string Path, bool IsFolder, long Size = 0, long Modified = 0)
{
    public string Name => PhoneFiles.NameOf(Path);

    public static PhoneFile? FromJson(JsonObject json)
    {
        if (json["path"].AsString() is not string raw || PhoneFiles.Clean(raw) is not string path) return null;

        return new PhoneFile(path, json["folder"].AsBool() ?? false, json["size"].AsLong() ?? 0, json["modified"].AsLong() ?? 0);
    }

    public JsonObject ToJson() => new()
    {
        ["path"] = Path,
        ["folder"] = IsFolder,
        ["size"] = Size,
        ["modified"] = Modified,
    };
}

/// Browsing and changing the phone's storage from a computer. The computer sends "flyylink.files.request" with an
/// action, and the phone answers each with "flyylink.files" carrying the same request id. Uploads carry the file as
/// the request's payload; downloads come back as the answer's payload. Mirrors PhoneFiles.kt and PhoneFiles.swift.
public static class PhoneFiles
{
    public enum Action { List, Download, Upload, Folder, Move, Delete }

    /// Why a request failed. `Denied` means the phone hasn't allowed computers to browse its files.
    public enum Failure { Denied, Missing, Exists, Invalid, Failed }

    /// `To` is where `Action.Move` puts the item.
    public sealed record Request(long Id, Action Action, string Path, string? To = null);

    /// `Files` is a folder's contents for `Action.List`; `File` is the item a request made, moved or sent.
    public sealed record Answer(long Id, Failure? Failure = null, IReadOnlyList<PhoneFile>? Files = null, PhoneFile? File = null)
    {
        public bool Ok => Failure is null;

        public IReadOnlyList<PhoneFile> Items => Files ?? [];
    }

    public const string Root = "/";

    private static readonly (Action Action, string Wire)[] Actions =
    [
        (Action.List, "list"),
        (Action.Download, "download"),
        (Action.Upload, "upload"),
        (Action.Folder, "folder"),
        (Action.Move, "move"),
        (Action.Delete, "delete"),
    ];

    private static readonly (Failure Failure, string Wire)[] Failures =
    [
        (Failure.Denied, "denied"),
        (Failure.Missing, "missing"),
        (Failure.Exists, "exists"),
        (Failure.Invalid, "invalid"),
        (Failure.Failed, "failed"),
    ];

    public static string Wire(this Action action) => Actions.First(a => a.Action == action).Wire;

    public static string Wire(this Failure failure) => Failures.First(f => f.Failure == failure).Wire;

    /// `path` with no empty or "." parts, starting with "/", or null when it would climb out of the root.
    public static string? Clean(string path)
    {
        if (path.Contains('\0')) return null;

        var parts = path.Split('/').Where(part => part.Length > 0 && part != ".").ToList();
        if (parts.Contains("..")) return null;

        return Root + string.Join('/', parts);
    }

    public static bool IsValidName(string name) =>
        name.Length > 0 && name != "." && name != ".." && !name.Contains('/') && !name.Contains('\0');

    public static string Join(string folder, string name) => folder == Root ? Root + name : $"{folder}/{name}";

    public static string NameOf(string path) => path[(path.LastIndexOf('/') + 1)..];

    public static string ParentOf(string path)
    {
        var slash = path.LastIndexOf('/');

        return slash <= 0 ? Root : path[..slash];
    }

    public static NetworkPacket RequestPacket(Request request)
    {
        var body = new JsonObject
        {
            ["id"] = request.Id,
            ["action"] = request.Action.Wire(),
            ["path"] = request.Path,
        };
        if (request.To is not null) body["to"] = request.To;

        return new NetworkPacket(PacketType.FilesRequest, body);
    }

    public static Request? RequestOf(NetworkPacket packet)
    {
        if (packet.Type != PacketType.FilesRequest || packet.GetLong("id") is not long id) return null;

        var wire = packet.GetString("action");
        if (Actions.Where(a => a.Wire == wire).Select(a => (Action?)a.Action).FirstOrDefault() is not Action action) return null;
        if (packet.GetString("path") is not string raw || Clean(raw) is not string path) return null;

        string? to = null;
        if (packet.GetString("to") is string rawTo && (to = Clean(rawTo)) is null) return null;
        if (action == Action.Move && (to is null || to == Root)) return null;

        return new Request(id, action, path, to);
    }

    public static NetworkPacket AnswerPacket(Answer answer)
    {
        var body = new JsonObject
        {
            ["id"] = answer.Id,
            ["ok"] = answer.Ok,
        };
        if (answer.Failure is Failure failure) body["error"] = failure.Wire();
        if (answer.Items.Count > 0) body["files"] = new JsonArray(answer.Items.Select(file => (JsonNode?)file.ToJson()).ToArray());
        if (answer.File is PhoneFile file) body["file"] = file.ToJson();

        return new NetworkPacket(PacketType.Files, body);
    }

    public static Answer? AnswerOf(NetworkPacket packet)
    {
        if (packet.Type != PacketType.Files || packet.GetLong("id") is not long id) return null;

        Failure? failure = null;
        if (packet.GetBool("ok") != true)
        {
            var wire = packet.GetString("error");
            failure = Failures.Where(f => f.Wire == wire).Select(f => (Failure?)f.Failure).FirstOrDefault() ?? Failure.Failed;
        }

        var files = (packet.Body["files"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(PhoneFile.FromJson)
            .OfType<PhoneFile>()
            .ToList();
        var item = packet.Body["file"] is JsonObject json ? PhoneFile.FromJson(json) : null;

        return new Answer(id, failure, files, item);
    }
}
