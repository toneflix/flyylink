using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Shell;

/// Records the main screen, pointer included, as JPEG pictures for another computer to view.
internal sealed class ScreenCapture : IDisposable
{
    private const int FramesPerSecond = 20;
    private const long Quality = 80;

    private static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    private readonly CancellationTokenSource stop = new();
    private readonly Size screen;
    private byte[]? last;

    public ScreenCapture(int maxSize)
    {
        var bounds = DesktopBounds.Primary();
        screen = new Size((int)bounds.MaxX + 1, (int)bounds.MaxY + 1);

        var scale = Math.Min(1, (double)maxSize / Math.Max(screen.Width, screen.Height));
        Size = new Size(Math.Max(2, (int)(screen.Width * scale) & ~1), Math.Max(2, (int)(screen.Height * scale) & ~1));
    }

    /// The pictures' size in pixels.
    public Size Size { get; }

    /// Called on a background thread with each picture that differs from the one before.
    public Action<byte[]>? OnPicture { get; set; }

    public void Start() => _ = Task.Run(RunAsync);

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
    }

    private async Task RunAsync()
    {
        var token = stop.Token;
        using var full = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppRgb);
        using var scaled = new Bitmap(Size.Width, Size.Height, PixelFormat.Format32bppRgb);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Quality);
        var interval = TimeSpan.FromSeconds(1.0 / FramesPerSecond);

        while (!token.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            try
            {
                if (Capture(full, scaled, parameters) is byte[] jpeg) OnPicture?.Invoke(jpeg);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // The secure desktop, such as the sign-in or a permission prompt, can't be captured.
            }

            var wait = interval - (DateTime.UtcNow - started);
            try
            {
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private byte[]? Capture(Bitmap full, Bitmap scaled, EncoderParameters parameters)
    {
        using (var graphics = Graphics.FromImage(full))
        {
            graphics.CopyFromScreen(0, 0, 0, 0, screen, CopyPixelOperation.SourceCopy);
            DrawPointer(graphics);
        }

        var picture = full;
        if (Size != screen)
        {
            using var graphics = Graphics.FromImage(scaled);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(full, 0, 0, Size.Width, Size.Height);
            picture = scaled;
        }

        using var stream = new MemoryStream();
        picture.Save(stream, Jpeg, parameters);
        var jpeg = stream.ToArray();

        // A still screen gives the same bytes; nothing new to send.
        if (last is not null && last.AsSpan().SequenceEqual(jpeg)) return null;

        last = jpeg;
        return jpeg;
    }

    private static void DrawPointer(Graphics graphics)
    {
        var info = new CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0 || !GetIconInfo(info.hCursor, out var icon)) return;

        DeleteObject(icon.hbmMask);
        DeleteObject(icon.hbmColor);

        var dc = graphics.GetHdc();
        try
        {
            DrawIconEx(dc, info.ptScreenPos.X - icon.xHotspot, info.ptScreenPos.Y - icon.yHotspot, info.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
        }
        finally
        {
            graphics.ReleaseHdc(dc);
        }
    }
}
