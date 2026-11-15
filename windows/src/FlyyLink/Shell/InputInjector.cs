using System.Runtime.InteropServices;
using FlyyLink.Core;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Shell;

/// Plays keyboard and mouse input from another computer or a phone on this PC. Safe to call from any thread.
internal sealed class InputInjector
{
    private readonly object gate = new();
    private readonly HashSet<MouseButton> buttons = [];
    private readonly HashSet<string> keys = [];
    private string? controllerId;

    /// The edge the pointer came in by, which it leaves by again. Null when it came from the screen viewer.
    private ScreenEdge? entry;
    private bool swapsControlAndMeta;
    private double x;
    private double y;

    /// Sends to a device; false when it isn't connected.
    public Func<string, RemoteInput, bool> Send { get; set; } = (_, _) => false;

    /// The computer now working this PC, or null. Called on any thread.
    public Action<string?>? OnChange { get; set; }

    public string? ControllerId
    {
        get
        {
            lock (gate) return controllerId;
        }
    }

    public void Handle(RemoteInput input, string deviceId)
    {
        lock (gate)
        {
            switch (input)
            {
                case RemoteInput.Enter enter:
                    Begin(deviceId, enter.Edge, enter.Platform);
                    var bounds = DesktopBounds.Current();
                    var (ex, ey) = enter.Edge.Point(enter.Position, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY, inset: 6);
                    MoveCursor(ex, ey);
                    break;
                case RemoteInput.Begin begin:
                    Begin(deviceId, null, begin.Platform);
                    break;
                case RemoteInput.MoveTo to:
                    if (controllerId != deviceId) Begin(deviceId, null, "");
                    var main = DesktopBounds.Primary();
                    MoveCursor(main.MinX + to.X * (main.MaxX - main.MinX), main.MinY + to.Y * (main.MaxY - main.MinY));
                    break;
                case RemoteInput.Release when controllerId == deviceId:
                    End();
                    break;
                case RemoteInput.Move move when controllerId == deviceId:
                    Move(move.Dx, move.Dy);
                    break;
                case RemoteInput.Button button when controllerId == deviceId:
                    Press(button.Which, button.Down);
                    break;
                case RemoteInput.Scroll scroll when controllerId == deviceId:
                    Scroll(scroll.Dx, scroll.Dy);
                    break;
                case RemoteInput.Key key when controllerId == deviceId:
                    Key(swapsControlAndMeta ? KeyCodes.SwappingControlAndMeta(key.Code) : key.Code, key.Down);
                    break;
                case RemoteInput.Text text when controllerId == deviceId:
                    Type(text.Value);
                    break;
            }
        }
    }

    /// The controlling computer went away: let go of everything it held.
    public void Lost(string deviceId)
    {
        lock (gate)
        {
            if (controllerId == deviceId) End();
        }
    }

    private void Begin(string deviceId, ScreenEdge? from, string platform)
    {
        if (controllerId is not null && controllerId != deviceId) End();

        controllerId = deviceId;
        entry = from;
        swapsControlAndMeta = platform == RemoteInput.MacOS;
        GetCursorPos(out var point);
        (x, y) = (point.X, point.Y);
        OnChange?.Invoke(deviceId);
    }

    private void End()
    {
        foreach (var button in buttons.ToList()) Press(button, false);
        foreach (var key in keys.ToList()) Key(key, false);

        controllerId = null;
        entry = null;
        OnChange?.Invoke(null);
    }

    private void Move(double dx, double dy)
    {
        var bounds = DesktopBounds.Current();
        var (tx, ty) = (x + dx, y + dy);

        if (entry is ScreenEdge edge && controllerId is string controller
            && edge.Reached(tx, ty, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY) && Heads(edge, dx, dy))
        {
            Send(controller, new RemoteInput.Leave(edge, edge.Position(tx, ty, bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY)));
            End();
            return;
        }

        var (cx, cy) = bounds.Clamp(tx, ty);
        MoveCursor(cx, cy);
    }

    private static bool Heads(ScreenEdge edge, double dx, double dy) => edge switch
    {
        ScreenEdge.Left => dx < 0,
        ScreenEdge.Right => dx > 0,
        ScreenEdge.Top => dy < 0,
        _ => dy > 0,
    };

    private void MoveCursor(double toX, double toY)
    {
        (x, y) = (toX, toY);

        var bounds = DesktopBounds.Current();
        var width = Math.Max(1, bounds.MaxX - bounds.MinX);
        var height = Math.Max(1, bounds.MaxY - bounds.MinY);
        var mouse = new MOUSEINPUT
        {
            dx = (int)Math.Round((toX - bounds.MinX) * 65535 / width),
            dy = (int)Math.Round((toY - bounds.MinY) * 65535 / height),
            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
        };

        Post(Mouse(mouse));
    }

    private void Press(MouseButton button, bool down)
    {
        if (down) buttons.Add(button);
        else buttons.Remove(button);

        var flags = (button, down) switch
        {
            (MouseButton.Left, true) => MOUSEEVENTF_LEFTDOWN,
            (MouseButton.Left, false) => MOUSEEVENTF_LEFTUP,
            (MouseButton.Right, true) => MOUSEEVENTF_RIGHTDOWN,
            (MouseButton.Right, false) => MOUSEEVENTF_RIGHTUP,
            (_, true) => MOUSEEVENTF_MIDDLEDOWN,
            _ => MOUSEEVENTF_MIDDLEUP,
        };

        Post(Mouse(new MOUSEINPUT { dwFlags = flags }));
    }

    /// About 30 points make one notch of the wheel.
    private static void Scroll(double dx, double dy)
    {
        if (dy != 0) Post(Mouse(new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = unchecked((uint)(int)Math.Round(dy * WHEEL_DELTA / 30)) }));
        if (dx != 0) Post(Mouse(new MOUSEINPUT { dwFlags = MOUSEEVENTF_HWHEEL, mouseData = unchecked((uint)(int)Math.Round(dx * WHEEL_DELTA / 30)) }));
    }

    private void Key(string code, bool down)
    {
        if (WindowsKeys.Lookup(code) is not WindowsKeys.Key key) return;

        if (down) keys.Add(code);
        else keys.Remove(code);

        var flags = (key.Extended ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
        Post(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = key.Vk, dwFlags = flags } } });
    }

    /// Types each character as itself, so text arrives right whatever the keyboard layout. New lines press Enter.
    private void Type(string text)
    {
        foreach (var unit in text.Replace("\r\n", "\n"))
        {
            if (unit == '\n')
            {
                Key("Enter", true);
                Key("Enter", false);
                continue;
            }

            foreach (var up in new[] { false, true })
            {
                var flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0);
                Post(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wScan = unit, dwFlags = flags } } });
            }
        }
    }

    private static INPUT Mouse(MOUSEINPUT mouse) => new() { type = INPUT_MOUSE, u = new INPUTUNION { mi = mouse } };

    private static void Post(INPUT input) => SendInput(1, [input], Marshal.SizeOf<INPUT>());
}
