using System.Text.Json.Nodes;

namespace FlyyLink.Core;

/// A side of a computer's screen, where the pointer crosses to another computer.
public enum ScreenEdge
{
    Left,
    Right,
    Top,
    Bottom,
}

public static class ScreenEdges
{
    public static string WireName(this ScreenEdge edge) => edge.ToString().ToLowerInvariant();

    public static ScreenEdge? Parse(string? name) =>
        name is not null && name == name.ToLowerInvariant() && Enum.TryParse<ScreenEdge>(name, ignoreCase: true, out var edge) && Enum.IsDefined(edge) ? edge : null;

    public static ScreenEdge Opposite(this ScreenEdge edge) => edge switch
    {
        ScreenEdge.Left => ScreenEdge.Right,
        ScreenEdge.Right => ScreenEdge.Left,
        ScreenEdge.Top => ScreenEdge.Bottom,
        _ => ScreenEdge.Top,
    };

    /// Where along this edge a point is, from 0 at the top or left to 1 at the bottom or right.
    public static double Position(this ScreenEdge edge, double x, double y, double minX, double minY, double maxX, double maxY)
    {
        var fraction = edge is ScreenEdge.Left or ScreenEdge.Right
            ? maxY > minY ? (y - minY) / (maxY - minY) : 0.5
            : maxX > minX ? (x - minX) / (maxX - minX) : 0.5;

        return Math.Clamp(fraction, 0, 1);
    }

    /// The point just inside this edge at `position`, `inset` away from it.
    public static (double X, double Y) Point(this ScreenEdge edge, double position, double minX, double minY, double maxX, double maxY, double inset = 2)
    {
        var along = Math.Clamp(position, 0, 1);

        return edge switch
        {
            ScreenEdge.Left => (minX + inset, minY + along * (maxY - minY)),
            ScreenEdge.Right => (maxX - inset, minY + along * (maxY - minY)),
            ScreenEdge.Top => (minX + along * (maxX - minX), minY + inset),
            _ => (minX + along * (maxX - minX), maxY - inset),
        };
    }

    /// True when the point is at or past this edge.
    public static bool Reached(this ScreenEdge edge, double x, double y, double minX, double minY, double maxX, double maxY) => edge switch
    {
        ScreenEdge.Left => x <= minX,
        ScreenEdge.Right => x >= maxX,
        ScreenEdge.Top => y <= minY,
        _ => y >= maxY,
    };
}

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// Keyboard and mouse shared from one computer to another ("flyylink.input"). The controlling computer sends `Enter`
/// when its pointer leaves through an edge, then moves, buttons, scrolls and keys. The controlled computer sends
/// `Leave` when its pointer goes back out through the edge it came in by. A phone sends `Begin` instead and works the
/// computer with its touchpad and keyboard. Keys use the W3C `code` names, such as
/// "KeyA" or "ShiftLeft". Mirrors Input.kt and Input.swift.
public abstract record RemoteInput
{
    public const string MacOS = "macos";
    public const string Windows = "windows";
    public const string Android = "android";

    /// Larger moves or scrolls than this in one packet are treated as nonsense.
    private const double MaxStep = 10_000;

    /// Longer text than this in one packet is treated as nonsense.
    public const int MaxText = 1000;

    /// The pointer arrives at the receiver's edge, `Position` along it. `Platform` is the sender's.
    public sealed record Enter(ScreenEdge Edge, double Position, string Platform) : RemoteInput;

    /// The sender works the receiver without an edge, as a phone does. The receiver answers with its own `Begin`, so the
    /// sender learns its `Platform`, and `Ready` is false while it can't play input.
    public sealed record Begin(string Platform, bool Ready = true) : RemoteInput;

    /// The pointer goes back to the sender, leaving the receiver through `Edge` at `Position`.
    public sealed record Leave(ScreenEdge Edge, double Position) : RemoteInput;

    /// The sender stopped controlling; let go of anything still held.
    public sealed record Release : RemoteInput;

    public sealed record Move(double Dx, double Dy) : RemoteInput;

    /// Moves the pointer to a fraction of the receiver's main screen, 0 to 1 from the top left.
    public sealed record MoveTo(double X, double Y) : RemoteInput;

    public sealed record Button(MouseButton Which, bool Down) : RemoteInput;

    /// Scrolls by this many points; positive `Dy` scrolls up and positive `Dx` right.
    public sealed record Scroll(double Dx, double Dy) : RemoteInput;

    public sealed record Key(string Code, bool Down) : RemoteInput;

    /// Types `Text` as it is, whatever the receiver's keyboard layout.
    public sealed record Text(string Value) : RemoteInput;

    public NetworkPacket ToPacket()
    {
        var body = this switch
        {
            Enter enter => new JsonObject { ["action"] = "enter", ["edge"] = enter.Edge.WireName(), ["position"] = enter.Position, ["platform"] = enter.Platform },
            Begin begin => new JsonObject { ["action"] = "begin", ["platform"] = begin.Platform, ["ready"] = begin.Ready },
            Leave leave => new JsonObject { ["action"] = "leave", ["edge"] = leave.Edge.WireName(), ["position"] = leave.Position },
            Release => new JsonObject { ["action"] = "release" },
            Move move => new JsonObject { ["action"] = "move", ["dx"] = move.Dx, ["dy"] = move.Dy },
            MoveTo to => new JsonObject { ["action"] = "moveTo", ["x"] = to.X, ["y"] = to.Y },
            Button button => new JsonObject { ["action"] = "button", ["button"] = button.Which.ToString().ToLowerInvariant(), ["down"] = button.Down },
            Scroll scroll => new JsonObject { ["action"] = "scroll", ["dx"] = scroll.Dx, ["dy"] = scroll.Dy },
            Key key => new JsonObject { ["action"] = "key", ["code"] = key.Code, ["down"] = key.Down },
            Text text => new JsonObject { ["action"] = "text", ["text"] = text.Value },
            _ => throw new InvalidOperationException(),
        };

        return new NetworkPacket(PacketType.Input, body);
    }

    public static RemoteInput? FromPacket(NetworkPacket packet)
    {
        if (packet.Type != PacketType.Input) return null;

        double? Fraction(string key) => packet.GetDouble(key) is double value and >= 0 and <= 1 ? value : null;
        double? Step(string key) => packet.GetDouble(key) is double value && double.IsFinite(value) && Math.Abs(value) <= MaxStep ? value : null;
        MouseButton? Which(string? name) => name switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => null,
        };

        return packet.GetString("action") switch
        {
            "enter" when ScreenEdges.Parse(packet.GetString("edge")) is ScreenEdge edge && Fraction("position") is double position =>
                new Enter(edge, position, packet.GetString("platform") ?? ""),
            "begin" => new Begin(packet.GetString("platform") ?? "", packet.GetBool("ready") ?? true),
            "leave" when ScreenEdges.Parse(packet.GetString("edge")) is ScreenEdge edge && Fraction("position") is double position => new Leave(edge, position),
            "release" => new Release(),
            "move" when Step("dx") is double dx && Step("dy") is double dy => new Move(dx, dy),
            "moveTo" when Fraction("x") is double x && Fraction("y") is double y => new MoveTo(x, y),
            "button" when Which(packet.GetString("button")) is MouseButton which && packet.GetBool("down") is bool down => new Button(which, down),
            "scroll" when Step("dx") is double dx && Step("dy") is double dy => new Scroll(dx, dy),
            "key" when packet.GetString("code") is string code && KeyCodes.All.Contains(code) && packet.GetBool("down") is bool down => new Key(code, down),
            "text" when packet.GetString("text") is { Length: > 0 and <= MaxText } text => new Text(text),
            _ => null,
        };
    }
}

/// The W3C `code` names keys travel under, and the ones that act as modifiers.
public static class KeyCodes
{
    public static readonly IReadOnlyList<string> Letters = Enumerable.Range('A', 26).Select(c => $"Key{(char)c}").ToList();

    public static readonly IReadOnlyList<string> Digits = Enumerable.Range(0, 10).Select(d => $"Digit{d}").ToList();

    public static readonly IReadOnlyList<string> FunctionKeys = Enumerable.Range(1, 12).Select(n => $"F{n}").ToList();

    public static readonly IReadOnlyList<string> Numpad =
        [.. Enumerable.Range(0, 10).Select(d => $"Numpad{d}"), "NumpadDecimal", "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadEnter"];

    public static readonly IReadOnlySet<string> Modifiers = new HashSet<string>
    {
        "ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight",
    };

    public static readonly IReadOnlyList<string> Others =
    [
        "Enter", "Escape", "Backspace", "Tab", "Space", "Minus", "Equal", "BracketLeft", "BracketRight", "Backslash",
        "Semicolon", "Quote", "Backquote", "Comma", "Period", "Slash", "IntlBackslash", "CapsLock",
        "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown", "Delete", "Insert", "ContextMenu",
    ];

    public static readonly IReadOnlyList<string> Media =
        ["AudioVolumeUp", "AudioVolumeDown", "AudioVolumeMute", "MediaPlayPause", "MediaTrackNext", "MediaTrackPrevious"];

    public static readonly IReadOnlySet<string> All = Letters.Concat(Digits).Concat(FunctionKeys).Concat(Numpad).Concat(Others).Concat(Media).Concat(Modifiers).ToHashSet();

    /// Swaps Control and Meta, so Ctrl+C on a PC is Command-C on a Mac and the other way round.
    public static string SwappingControlAndMeta(string code) => code switch
    {
        "ControlLeft" => "MetaLeft",
        "ControlRight" => "MetaRight",
        "MetaLeft" => "ControlLeft",
        "MetaRight" => "ControlRight",
        _ => code,
    };
}
