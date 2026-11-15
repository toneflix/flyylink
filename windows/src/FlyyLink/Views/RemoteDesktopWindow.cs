using FlyyLink.Core;
using FlyyLink.Icons;
using FlyyLink.Model;
using FlyyLink.Shell;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Views;

/// Another computer's screen. The pointer, clicks, scrolling and typing over it go to that computer.
/// Closing it stops watching.
public sealed class RemoteDesktopWindow : Window
{
    private readonly RemoteDesktop desktop;
    private readonly Image picture = new() { Stretch = Stretch.Uniform };
    private readonly SoftwareBitmapSource source = new();
    private readonly Grid surface = new() { Background = new SolidColorBrush(Colors.Black), IsTabStop = true };
    private readonly StackPanel status = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320 };
    private readonly LucideIcon statusIcon = new() { Size = 40, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock statusTitle = new() { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) };
    private readonly TextBlock statusHint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Geometries.Argb(0xB3FFFFFF)) };
    private readonly ProgressRing waiting = new() { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button retry = new() { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(10) };
    private readonly HashSet<string> keys = [];
    private readonly HashSet<MouseButton> buttons = [];
    private bool hasPicture;
    private bool shown;
    private bool showing;
    private SoftwareBitmap? next;

    public RemoteDesktopWindow(RemoteDesktop desktop)
    {
        this.desktop = desktop;
        Title = "Screen";

        AppWindow.SetIcon(AppPaths.WindowIcon);
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            ReleaseAll();
            desktop.Stop();
            AppWindow.Hide();
            shown = false;
        };
        if (AppWindow.TitleBar is { } bar)
        {
            bar.BackgroundColor = Colors.Black;
            bar.ButtonBackgroundColor = Colors.Black;
            bar.ForegroundColor = Colors.White;
            bar.ButtonForegroundColor = Colors.White;
            bar.InactiveBackgroundColor = Colors.Black;
            bar.ButtonInactiveBackgroundColor = Colors.Black;
        }
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) ReleaseAll();
        };

        picture.Source = source;
        statusIcon.SetValue(LucideIcon.ForegroundProperty, Application.Current.Resources["FlyyLinkAccentBrush"]);
        retry.Click += (_, _) =>
        {
            if (desktop.DeviceId is string id) desktop.Begin(id, desktop.DeviceName);
        };
        foreach (var child in new UIElement[] { statusIcon, statusTitle, statusHint, waiting, retry }) status.Children.Add(child);

        surface.Children.Add(picture);
        surface.Children.Add(status);
        surface.PointerMoved += Surface_PointerMoved;
        surface.PointerPressed += Surface_PointerPressed;
        surface.PointerReleased += Surface_PointerReleased;
        surface.PointerWheelChanged += Surface_PointerWheelChanged;
        surface.KeyDown += (_, e) => Key(e, down: true);
        surface.KeyUp += (_, e) => Key(e, down: false);
        surface.LosingFocus += (_, _) => ReleaseAll();

        Content = new Grid { Background = new SolidColorBrush(Colors.Black), RequestedTheme = ElementTheme.Dark, Children = { surface } };

        desktop.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RemoteDesktop.Phase) && desktop.Phase != MirrorPhase.Live) hasPicture = false;
            if (args.PropertyName is nameof(RemoteDesktop.Phase) or nameof(RemoteDesktop.DeviceName)) ShowStatus();
            if (args.PropertyName == nameof(RemoteDesktop.Size)) Fit();
        };
        ShowStatus();
    }

    /// Shows a decoded picture. Call on the UI thread; pictures that arrive while one is being set replace each other.
    internal async void Show(SoftwareBitmap bitmap)
    {
        if (desktop.Phase != MirrorPhase.Live) return;

        next?.Dispose();
        next = bitmap;
        if (showing) return;

        showing = true;
        while (next is { } latest)
        {
            next = null;
            await source.SetBitmapAsync(latest);
            latest.Dispose();
        }
        showing = false;

        if (!hasPicture)
        {
            hasPicture = true;
            ShowStatus();
        }
    }

    public void Present()
    {
        Title = $"{desktop.DeviceName}'s screen";
        if (!shown)
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(1100 * scale), (int)(700 * scale)));
            shown = true;
        }

        AppWindow.Show(true);
        Activate();
        surface.Focus(FocusState.Programmatic);
    }

    private void ShowStatus()
    {
        var phase = desktop.Phase;
        Title = $"{desktop.DeviceName}'s screen";
        status.Visibility = phase == MirrorPhase.Live && hasPicture ? Visibility.Collapsed : Visibility.Visible;
        picture.Visibility = phase == MirrorPhase.Live ? Visibility.Visible : Visibility.Collapsed;
        statusIcon.Icon = phase == MirrorPhase.Ended ? Lucide.Unplug : Lucide.ScreenShare;
        statusTitle.Text = phase switch
        {
            MirrorPhase.Ended => "Screen sharing stopped",
            MirrorPhase.Idle => "Not connected",
            _ => $"Connecting to {desktop.DeviceName}",
        };
        statusHint.Text = phase switch
        {
            MirrorPhase.Ended => $"{desktop.DeviceName} stopped sharing. It needs Screen Recording access (on a Mac) and \"Let my devices control\" on.",
            MirrorPhase.Idle => "Start from View screen in the FlyyLink panel.",
            _ => "Connecting…",
        };
        var busy = phase == MirrorPhase.Waiting || (phase == MirrorPhase.Live && !hasPicture);
        waiting.IsActive = busy;
        waiting.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        retry.Visibility = phase == MirrorPhase.Ended && desktop.DeviceId is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// Keeps the window the shape of the picture, keeping its width.
    private void Fit()
    {
        var (width, height) = desktop.Size;
        if (width <= 0 || height <= 0) return;

        var client = AppWindow.ClientSize;
        AppWindow.ResizeClient(new SizeInt32(client.Width, (int)Math.Round((double)client.Width * height / width)));
    }

    // Pointer and keyboard

    /// Where the picture sits inside the surface, since it keeps its shape.
    private Windows.Foundation.Rect PictureRect()
    {
        var (width, height) = desktop.Size;
        var area = new Windows.Foundation.Rect(0, 0, surface.ActualWidth, surface.ActualHeight);
        if (width <= 0 || height <= 0 || area.Width <= 0 || area.Height <= 0) return area;

        var fit = Math.Min(area.Width / width, area.Height / height);
        var w = width * fit;
        var h = height * fit;

        return new Windows.Foundation.Rect((area.Width - w) / 2, (area.Height - h) / 2, w, h);
    }

    private void MoveTo(Windows.Foundation.Point point)
    {
        var rect = PictureRect();
        if (rect.Width <= 0 || rect.Height <= 0) return;

        desktop.Input(new RemoteInput.MoveTo(Math.Clamp((point.X - rect.X) / rect.Width, 0, 1), Math.Clamp((point.Y - rect.Y) / rect.Height, 0, 1)));
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e) => MoveTo(e.GetCurrentPoint(surface).Position);

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        surface.Focus(FocusState.Pointer);
        surface.CapturePointer(e.Pointer);
        Press(e, down: true);
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Press(e, down: false);
        if (buttons.Count == 0) surface.ReleasePointerCapture(e.Pointer);
    }

    private void Press(PointerRoutedEventArgs e, bool down)
    {
        var point = e.GetCurrentPoint(surface);
        MouseButton? button = point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => MouseButton.Left,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => MouseButton.Right,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => MouseButton.Middle,
            _ => null,
        };
        if (button is not MouseButton which) return;

        e.Handled = true;
        MoveTo(point.Position);
        if (down) buttons.Add(which);
        else buttons.Remove(which);
        desktop.Input(new RemoteInput.Button(which, down));
    }

    /// One wheel notch is about 30 points of scrolling on the other side.
    private void Surface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(surface).Properties;
        var points = properties.MouseWheelDelta * 30.0 / WHEEL_DELTA;
        e.Handled = true;

        desktop.Input(properties.IsHorizontalMouseWheel ? new RemoteInput.Scroll(points, 0) : new RemoteInput.Scroll(0, points));
    }

    private void Key(KeyRoutedEventArgs e, bool down)
    {
        if (KeyName(e) is not string name) return;

        e.Handled = true;
        var code = desktop.SwapsControlAndMeta ? KeyCodes.SwappingControlAndMeta(name) : name;
        if (!down && !keys.Contains(code)) return;

        if (down) keys.Add(code);
        else keys.Remove(code);
        desktop.Input(new RemoteInput.Key(code, down));
    }

    /// XAML reports Shift, Ctrl and Alt without their side, so the scan code and extended flag tell left from right.
    private static string? KeyName(KeyRoutedEventArgs e)
    {
        var status = e.KeyStatus;
        var vk = (uint)e.Key switch
        {
            0x10 => status.ScanCode == 0x36 ? 0xA1u : 0xA0u,
            0x11 => status.IsExtendedKey ? 0xA3u : 0xA2u,
            0x12 => status.IsExtendedKey ? 0xA5u : 0xA4u,
            var other => other,
        };

        return WindowsKeys.Name(vk, status.IsExtendedKey);
    }

    /// Lets go of everything held, so no key or button stays down over there.
    private void ReleaseAll()
    {
        foreach (var key in keys) desktop.Input(new RemoteInput.Key(key, false));
        foreach (var button in buttons) desktop.Input(new RemoteInput.Button(button, false));
        keys.Clear();
        buttons.Clear();
    }
}
