using FlyyLink.Core;
using FlyyLink.Icons;
using FlyyLink.Model;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.System;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Views;

/// The phone's live screen and sound, with Back, Home, Recents, Notifications and Mute underneath. Clicks become
/// taps, drags swipes, the wheel scrolls, right-click goes back and typing goes to the phone. Closing it stops mirroring.
public sealed class ScreenWindow : Window
{
    private const double ControlsHeight = 52;
    private const double DragDistance = 6;
    private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(500);

    private readonly ScreenMirror mirror;
    private readonly Windows.Media.Playback.MediaPlayer player = new() { RealTimePlayback = true, AutoPlay = true, IsMuted = true };
    private readonly MediaPlayerElement surface = new() { Stretch = Stretch.Uniform, AreTransportControlsEnabled = false };
    private readonly Grid touch = new() { Background = new SolidColorBrush(Colors.Transparent), IsTabStop = true };
    private readonly StackPanel status = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 280 };
    private readonly LucideIcon statusIcon = new() { Size = 40 };
    private readonly TextBlock statusTitle = new() { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) };
    private readonly TextBlock statusHint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Geometries.Argb(0xB3FFFFFF)) };
    private readonly ProgressRing waiting = new() { Width = 20, Height = 20 };
    private readonly Button retry = new() { Content = "Try again", HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(10) };
    private (Windows.Foundation.Point Point, DateTimeOffset Time)? pressed;
    private double scrolled;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer scrollEnd;
    private bool shown;
    private readonly Button muteButton;
    private readonly LucideIcon muteIcon = new() { Size = 18, Foreground = new SolidColorBrush(Colors.White) };

    public ScreenWindow(ScreenMirror mirror)
    {
        this.mirror = mirror;
        Title = "Phone screen";

        AppWindow.SetIcon(AppPaths.WindowIcon);
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            mirror.Stop();
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

        surface.SetMediaPlayer(player);

        muteButton = RoundButton(muteIcon);
        muteButton.Click += (_, _) => mirror.IsMuted = !mirror.IsMuted;
        ShowMute();

        statusIcon.SetValue(LucideIcon.ForegroundProperty, Application.Current.Resources["FlyyLinkAccentBrush"]);
        retry.Click += (_, _) =>
        {
            if (mirror.DeviceId is string id) mirror.Begin(id, mirror.DeviceName);
        };
        foreach (var child in new UIElement[] { statusIcon, statusTitle, statusHint, waiting, retry }) status.Children.Add(child);
        statusIcon.HorizontalAlignment = HorizontalAlignment.Center;
        waiting.HorizontalAlignment = HorizontalAlignment.Center;

        scrollEnd = DispatcherQueue.CreateTimer();
        scrollEnd.Interval = TimeSpan.FromMilliseconds(120);
        scrollEnd.IsRepeating = false;
        scrollEnd.Tick += (_, _) => SendScroll();

        touch.PointerPressed += Touch_PointerPressed;
        touch.PointerReleased += Touch_PointerReleased;
        touch.PointerWheelChanged += Touch_PointerWheelChanged;
        touch.KeyDown += Touch_KeyDown;
        touch.CharacterReceived += Touch_CharacterReceived;

        var screen = new Grid { Background = new SolidColorBrush(Colors.Black), Children = { surface, touch, status } };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(Control(Lucide.ChevronLeft, "Back (Esc or right-click)", ScreenInput.Back));
        controls.Children.Add(Control(Lucide.Circle, "Home", ScreenInput.Home));
        controls.Children.Add(Control(Lucide.Copy, "Recent apps", ScreenInput.Recents));
        controls.Children.Add(Control(Lucide.Bell, "Notifications", ScreenInput.Notifications));
        controls.Children.Add(muteButton);

        var root = new Grid { Background = new SolidColorBrush(Colors.Black), RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ControlsHeight) });
        Grid.SetRow(controls, 1);
        root.Children.Add(screen);
        root.Children.Add(controls);
        Content = root;

        mirror.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ScreenMirror.Phase) or nameof(ScreenMirror.DeviceName)) ShowStatus();
            if (args.PropertyName == nameof(ScreenMirror.Size)) Fit();
            if (args.PropertyName == nameof(ScreenMirror.IsMuted)) ShowMute();
        };
        ShowStatus();
    }

    /// Plays a new stream.
    internal void Play(MediaStreamSource source)
    {
        player.Source = MediaSource.CreateFromMediaStreamSource(source);
        player.Play();
    }

    public void Present()
    {
        if (!shown)
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(400 * scale), (int)(820 * scale)));
            shown = true;
        }

        AppWindow.Show(true);
        Activate();
        touch.Focus(FocusState.Programmatic);
    }

    private void ShowStatus()
    {
        var phase = mirror.Phase;
        status.Visibility = phase == MirrorPhase.Live ? Visibility.Collapsed : Visibility.Visible;
        statusIcon.Icon = phase == MirrorPhase.Ended ? Lucide.Unplug : Lucide.Cast;
        statusTitle.Text = phase switch
        {
            MirrorPhase.Ended => "Mirroring stopped",
            MirrorPhase.Idle => "Not mirroring",
            _ => $"Allow on {mirror.DeviceName}",
        };
        statusHint.Text = phase switch
        {
            MirrorPhase.Ended => "Your phone stopped sharing its screen or disconnected.",
            MirrorPhase.Idle => "Start it from Mirror in the FlyyLink panel.",
            _ => "Tap the notification on your phone, then \"Start now\".",
        };
        waiting.IsActive = phase == MirrorPhase.Waiting;
        waiting.Visibility = phase == MirrorPhase.Waiting ? Visibility.Visible : Visibility.Collapsed;
        retry.Visibility = phase == MirrorPhase.Ended && mirror.DeviceId is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// Keeps the window the shape of the phone's picture, keeping its height.
    private void Fit()
    {
        var (width, height) = mirror.Size;
        if (width <= 0 || height <= 0) return;

        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var client = AppWindow.ClientSize;
        var pictureHeight = client.Height - ControlsHeight * scale;
        var newWidth = (int)Math.Round(pictureHeight * width / height);
        AppWindow.ResizeClient(new SizeInt32(Math.Max(newWidth, 200), client.Height));
    }

    private Button Control(Lucide icon, string label, string key)
    {
        var button = RoundButton(new LucideIcon { Icon = icon, Size = 18, Foreground = new SolidColorBrush(Colors.White) });
        button.Click += (_, _) => mirror.Input(new ScreenInput.KeyPress(key));
        Label(button, label);

        return button;
    }

    private static Button RoundButton(UIElement content) => new()
    {
        Width = 40,
        Height = 40,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(20),
        Background = new SolidColorBrush(Colors.Transparent),
        BorderThickness = new Thickness(0),
        Content = content,
    };

    private static void Label(Button button, string label)
    {
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
    }

    private void ShowMute()
    {
        muteIcon.Icon = mirror.IsMuted ? Lucide.VolumeX : Lucide.Volume2;
        Label(muteButton, mirror.IsMuted ? "Play the phone's sound" : "Mute the phone's sound");
    }

    // Pointer and keyboard

    /// Where the picture sits inside the surface, since it keeps its shape.
    private Windows.Foundation.Rect Picture()
    {
        var (width, height) = mirror.Size;
        var area = new Windows.Foundation.Rect(0, 0, touch.ActualWidth, touch.ActualHeight);
        if (width <= 0 || height <= 0 || area.Width <= 0 || area.Height <= 0) return area;

        var fit = Math.Min(area.Width / width, area.Height / height);
        var w = width * fit;
        var h = height * fit;

        return new Windows.Foundation.Rect((area.Width - w) / 2, (area.Height - h) / 2, w, h);
    }

    private (double X, double Y) Fraction(Windows.Foundation.Point point)
    {
        var picture = Picture();

        return (Math.Clamp((point.X - picture.X) / Math.Max(picture.Width, 1), 0, 1), Math.Clamp((point.Y - picture.Y) / Math.Max(picture.Height, 1), 0, 1));
    }

    private void Touch_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        touch.Focus(FocusState.Pointer);

        var point = e.GetCurrentPoint(touch);
        if (point.Properties.IsRightButtonPressed)
        {
            mirror.Input(new ScreenInput.KeyPress(ScreenInput.Back));
            return;
        }

        if (!Picture().Contains(point.Position)) return;

        pressed = (point.Position, DateTimeOffset.Now);
        touch.CapturePointer(e.Pointer);
    }

    private void Touch_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        touch.ReleasePointerCapture(e.Pointer);
        if (pressed is not { } start) return;

        pressed = null;
        var end = e.GetCurrentPoint(touch).Position;
        var held = DateTimeOffset.Now - start.Time;
        var from = Fraction(start.Point);

        if (Math.Sqrt(Math.Pow(end.X - start.Point.X, 2) + Math.Pow(end.Y - start.Point.Y, 2)) < DragDistance)
        {
            mirror.Input(held >= LongPress ? new ScreenInput.LongPress(from.X, from.Y) : new ScreenInput.Tap(from.X, from.Y));
            return;
        }

        var to = Fraction(end);
        var duration = (long)(Math.Clamp(held.TotalSeconds, 0.08, 2) * 1000);
        mirror.Input(new ScreenInput.Swipe(from.X, from.Y, to.X, to.Y, duration));
    }

    /// The wheel becomes one swipe from the middle, sent once it settles.
    private void Touch_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        scrolled += e.GetCurrentPoint(touch).Properties.MouseWheelDelta;
        e.Handled = true;

        scrollEnd.Stop();
        scrollEnd.Start();
    }

    private void SendScroll()
    {
        var picture = Picture();
        var distance = Math.Clamp(scrolled / Math.Max(picture.Height, 1) * 0.6, -0.45, 0.45);
        scrolled = 0;
        if (Math.Abs(distance) < 0.01) return;

        mirror.Input(new ScreenInput.Swipe(0.5, 0.5 - distance / 2, 0.5, 0.5 + distance / 2, 250));
    }

    private void Touch_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        string? key = e.Key switch
        {
            VirtualKey.Back or VirtualKey.Delete => ScreenInput.Delete,
            VirtualKey.Enter => ScreenInput.Enter,
            VirtualKey.Escape => ScreenInput.Back,
            _ => null,
        };
        if (key is null) return;

        e.Handled = true;
        mirror.Input(new ScreenInput.KeyPress(key));
    }

    private void Touch_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (char.IsControl(e.Character)) return;

        e.Handled = true;
        mirror.Input(new ScreenInput.Text(e.Character.ToString()));
    }
}
