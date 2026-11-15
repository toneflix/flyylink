using FlyyLink.Core;

namespace FlyyLink.Model;

/// Watching and working another computer's screen, for the remote desktop window. Use on the UI thread.
public sealed class RemoteDesktop : Observable
{
    /// The longer side of this PC's main screen in pixels, so the picture is never sent larger than it can be shown.
    public static long MaxSize
    {
        get
        {
            var screen = Shell.DesktopBounds.Primary();

            return Math.Clamp((long)Math.Max(screen.MaxX, screen.MaxY) + 1, 1280, 3840);
        }
    }

    private MirrorPhase phase;
    private string deviceName = "";
    private (long Width, long Height) size;

    /// Sends a packet to a device. Set by the app model.
    internal Func<string, NetworkPacket, bool>? Send { get; set; }

    public string? DeviceId { get; private set; }

    /// The other computer's platform, so Control and Command can be swapped when they differ.
    public string? Platform { get; private set; }

    public MirrorPhase Phase
    {
        get => phase;
        private set => Set(ref phase, value);
    }

    public string DeviceName
    {
        get => deviceName;
        private set => Set(ref deviceName, value);
    }

    /// The picture's size in pixels, once the other computer said it.
    public (long Width, long Height) Size
    {
        get => size;
        private set => Set(ref size, value);
    }

    /// Whether Control and Command trade places on the way, so shortcuts feel the same on both sides.
    public bool SwapsControlAndMeta => Platform == RemoteInput.MacOS;

    public void Begin(string id, string name)
    {
        if (Phase == MirrorPhase.Live && DeviceId == id) return;

        if (DeviceId is string old && old != id) Leave(old);
        DeviceId = id;
        DeviceName = name;
        Phase = Send?.Invoke(id, Screen.Start(MaxSize)) == true ? MirrorPhase.Waiting : MirrorPhase.Ended;
    }

    public void Stop()
    {
        if (DeviceId is not string id) return;

        Leave(id);
        DeviceId = null;
        Phase = MirrorPhase.Idle;
    }

    public void Input(RemoteInput input)
    {
        if (Phase == MirrorPhase.Live && DeviceId is string id) Send?.Invoke(id, input.ToPacket());
    }

    public void Started(string id, string name, long width, long height, string? platform)
    {
        if (DeviceId is string old && old != id) Leave(old);

        DeviceId = id;
        DeviceName = name;
        Platform = platform;
        Size = (width, height);
        Phase = MirrorPhase.Live;
    }

    public void Ended(string id)
    {
        if (id == DeviceId && Phase != MirrorPhase.Idle) Phase = MirrorPhase.Ended;
    }

    /// Stops the stream and lets go of the other computer's keyboard and mouse.
    private void Leave(string id)
    {
        Send?.Invoke(id, new RemoteInput.Release().ToPacket());
        Send?.Invoke(id, Screen.Stop());
    }
}
