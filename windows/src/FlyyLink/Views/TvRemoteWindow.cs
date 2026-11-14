using FlyyLink.Core;
using FlyyLink.Icons;
using FlyyLink.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Views;

/// A remote for a paired TV: the D-pad, Back, Home and volume, a touchpad for the TV's pointer,
/// and the keyboard's keys typed on the TV. Mirrors TvRemoteWindow.swift.
public sealed class TvRemoteWindow : Window
{
    private readonly AppModel model;
    private readonly Grid pad = new() { Height = 190, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), IsTabStop = true };
    private readonly TextBlock padTitle = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock playingTitle = new() { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock playingArtist = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border playing = new() { Style = (Style)Application.Current.Resources["FlyyLinkCard"], Padding = new Thickness(12) };
    private readonly StackPanel controls = new() { Spacing = 16 };
    private readonly StackPanel gone = new() { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private string? deviceId;
    private bool shown;

    // The pointer is held where it was clicked, and each move is read from there and put back.
    private bool holding;
    private POINT anchor;
    private double moveX, moveY, scrollX, scrollY;
    private bool movePending, scrollPending;
    private char? highSurrogate;

    public TvRemoteWindow(AppModel model)
    {
        this.model = model;
        Title = "Remote";

        AppWindow.SetIcon(AppPaths.WindowIcon);
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            LetGo();
            AppWindow.Hide();
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) LetGo();
        };

        BuildPad();
        playing.Child = new StackPanel { Spacing = 1, Children = { playingTitle, playingArtist } };
        playingArtist.SetValue(TextBlock.ForegroundProperty, Application.Current.Resources["FlyyLinkSubBrush"]);

        var dpad = DPad();
        var side = new StackPanel
        {
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Row(KeyButton(Lucide.ArrowLeft, "Back", TvRemote.Back), KeyButton(Lucide.House, "Home", TvRemote.Home)),
                Row(KeyButton(Lucide.Ellipsis, "Menu", TvRemote.Menu), KeyButton(Lucide.VolumeX, "Mute", TvRemote.Mute)),
                Row(KeyButton(Lucide.Volume1, "Volume down", TvRemote.VolumeDown), KeyButton(Lucide.Volume2, "Volume up", TvRemote.VolumeUp)),
            },
        };
        var middle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center, Children = { dpad, side } };
        var media = Row(
            KeyButton(Lucide.SkipBack, "Previous", TvRemote.Previous),
            KeyButton(Lucide.Play, "Play or pause", TvRemote.PlayPause, width: 76),
            KeyButton(Lucide.SkipForward, "Next", TvRemote.Next));
        media.HorizontalAlignment = HorizontalAlignment.Center;

        foreach (var child in new UIElement[] { playing, pad, middle, media }) controls.Children.Add(child);

        var unplug = new LucideIcon { Icon = Lucide.Unplug, Size = 36, HorizontalAlignment = HorizontalAlignment.Center };
        unplug.SetValue(LucideIcon.ForegroundProperty, Application.Current.Resources["FlyyLinkAccentBrush"]);
        gone.Children.Add(unplug);
        gone.Children.Add(new TextBlock { Text = "TV not connected", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });

        var root = new Grid { Padding = new Thickness(20), Children = { controls, gone } };
        root.SetValue(Panel.BackgroundProperty, Application.Current.Resources["FlyyLinkPanelBrush"]);
        Content = root;

        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppModel.Devices)) Update();
        };
    }

    public void Present(string id)
    {
        deviceId = id;
        Update();

        if (!shown)
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(400 * scale), (int)(640 * scale)));
            shown = true;
        }

        AppWindow.Show(true);
        Activate();
        pad.Focus(FocusState.Programmatic);
    }

    private DeviceState? Device => model.Devices.FirstOrDefault(d => d.Id == deviceId && d.IsPaired);

    private void Update()
    {
        var device = Device;
        Title = device is null ? "Remote" : $"{device.Name} remote";
        controls.Visibility = device is null ? Visibility.Collapsed : Visibility.Visible;
        gone.Visibility = device is null ? Visibility.Visible : Visibility.Collapsed;

        var player = device?.Media?.Player;
        playing.Visibility = player is null ? Visibility.Collapsed : Visibility.Visible;
        playingTitle.Text = player?.Title ?? "";
        playingArtist.Text = player?.Artist ?? "";
        playingArtist.Visibility = string.IsNullOrEmpty(player?.Artist) ? Visibility.Collapsed : Visibility.Visible;
        if (device is null) LetGo();
    }

    private void Press(TvRemote press)
    {
        if (deviceId is string id) model.Press(id, press);
    }

    // Buttons

    private Button KeyButton(Lucide icon, string name, string key, double width = 40, bool round = false)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources[round ? "FlyyLinkPlainButton" : "FlyyLinkTileButton"],
            Width = width,
            Height = 40,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(round ? 20 : 12),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new LucideIcon { Icon = icon, Size = 15 },
            IsTabStop = false,
        };
        ToolTipService.SetToolTip(button, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => Press(new TvRemote.Key(key));
        return button;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    /// Up, down, left, right and OK in a ring, like a TV remote.
    private Grid DPad()
    {
        var ring = new Grid { Width = 164, Height = 164 };
        ring.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(82),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["FlyyLinkCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["FlyyLinkBorderBrush"],
        });

        void Arrow(Lucide icon, string name, string key, HorizontalAlignment x, VerticalAlignment y)
        {
            var button = KeyButton(icon, name, key, round: true);
            button.HorizontalAlignment = x;
            button.VerticalAlignment = y;
            button.Margin = new Thickness(6);
            ring.Children.Add(button);
        }

        Arrow(Lucide.ChevronUp, "Up", TvRemote.Up, HorizontalAlignment.Center, VerticalAlignment.Top);
        Arrow(Lucide.ChevronDown, "Down", TvRemote.Down, HorizontalAlignment.Center, VerticalAlignment.Bottom);
        Arrow(Lucide.ChevronLeft, "Left", TvRemote.Left, HorizontalAlignment.Left, VerticalAlignment.Center);
        Arrow(Lucide.ChevronRight, "Right", TvRemote.Right, HorizontalAlignment.Right, VerticalAlignment.Center);

        var ok = new Button
        {
            Style = (Style)Application.Current.Resources["FlyyLinkPillAccentButton"],
            Width = 58,
            Height = 58,
            CornerRadius = new CornerRadius(29),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Content = "OK",
            IsTabStop = false,
        };
        ok.Click += (_, _) => Press(new TvRemote.Key(TvRemote.Select));
        ring.Children.Add(ok);

        return ring;
    }

    // Touchpad

    private void BuildPad()
    {
        pad.SetValue(Panel.BackgroundProperty, Application.Current.Resources["FlyyLinkCardBrush"]);
        pad.SetValue(Grid.BorderBrushProperty, Application.Current.Resources["FlyyLinkBorderBrush"]);

        var icon = new LucideIcon { Icon = Lucide.MousePointer2, Size = 22, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetValue(LucideIcon.ForegroundProperty, Application.Current.Resources["FlyyLinkAccentBrush"]);
        var hint = new TextBlock { Text = "Keys go to the TV", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        hint.SetValue(TextBlock.ForegroundProperty, Application.Current.Resources["FlyyLinkSubBrush"]);
        pad.Children.Add(new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Children = { icon, padTitle, hint },
        });
        ShowHolding();

        pad.PointerPressed += Pad_PointerPressed;
        pad.PointerMoved += (_, _) => Moved();
        pad.PointerWheelChanged += Pad_PointerWheelChanged;
        pad.KeyDown += Pad_KeyDown;
        pad.CharacterReceived += Pad_CharacterReceived;
        pad.LosingFocus += (_, _) => LetGo();
    }

    private void ShowHolding()
    {
        padTitle.Text = holding ? "Press Ctrl to let go" : "Click to use the pointer";
        pad.BorderThickness = new Thickness(holding ? 2 : 1);
        pad.SetValue(Grid.BorderBrushProperty, Application.Current.Resources[holding ? "FlyyLinkAccentBrush" : "FlyyLinkBorderBrush"]);
    }

    private void Pad_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        pad.Focus(FocusState.Pointer);

        var point = e.GetCurrentPoint(pad);
        if (!holding)
        {
            Hold(point.Position);
            return;
        }

        if (point.Properties.IsRightButtonPressed) Press(new TvRemote.Key(TvRemote.Back));
        else Press(new TvRemote.Click());
    }

    /// Keeps this PC's pointer inside the touchpad and hides it, so the mouse or touchpad moves the TV's pointer instead.
    private void Hold(Windows.Foundation.Point at)
    {
        if (!GetCursorPos(out anchor)) return;

        var scale = pad.XamlRoot.RasterizationScale;
        var left = anchor.X - (int)(at.X * scale);
        var top = anchor.Y - (int)(at.Y * scale);
        var bounds = new RECT { Left = left, Top = top, Right = left + (int)(pad.ActualWidth * scale), Bottom = top + (int)(pad.ActualHeight * scale) };

        ClipCursor(ref bounds);
        ShowCursor(false);
        holding = true;
        ShowHolding();
    }

    /// Gives this PC its pointer back.
    private void LetGo()
    {
        if (!holding) return;

        holding = false;
        ClipCursor(IntPtr.Zero);
        ShowCursor(true);
        ShowHolding();
    }

    private void Moved()
    {
        if (!holding || !GetCursorPos(out var now)) return;

        var scale = pad.XamlRoot.RasterizationScale;
        var dx = (now.X - anchor.X) / scale;
        var dy = (now.Y - anchor.Y) / scale;
        if (dx == 0 && dy == 0) return;

        SetCursorPos(anchor.X, anchor.Y);
        moveX += dx;
        moveY += dy;
        FlushMove();
    }

    /// Moves go out about 60 times a second, like the phone's touchpad.
    private async void FlushMove()
    {
        if (movePending) return;

        movePending = true;
        await Task.Delay(16);
        movePending = false;

        Press(new TvRemote.Move(Step(moveX), Step(moveY)));
        moveX = moveY = 0;
    }

    /// One wheel notch is about 30 points. Each scroll is a swipe on the TV, so they go out about 10 times a second.
    private async void Pad_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        var properties = e.GetCurrentPoint(pad).Properties;
        var points = properties.MouseWheelDelta * 30.0 / WHEEL_DELTA;
        if (properties.IsHorizontalMouseWheel) scrollX -= points;
        else scrollY += points;

        if (scrollPending) return;

        scrollPending = true;
        await Task.Delay(100);
        scrollPending = false;

        if (scrollX != 0 || scrollY != 0) Press(new TvRemote.Scroll(Step(scrollX), Step(scrollY)));
        scrollX = scrollY = 0;
    }

    private static double Step(double value) => Math.Clamp(value, -TvRemote.MaxStep, TvRemote.MaxStep);

    // Keys

    private void Pad_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Control)
        {
            LetGo();
            return;
        }

        if (Key(e.Key) is not string key) return;

        e.Handled = true;
        Press(new TvRemote.Key(key));
    }

    private void Pad_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        var character = e.Character;
        if (char.IsControl(character)) return;

        e.Handled = true;

        // Emoji and other characters outside the BMP arrive in two halves.
        if (char.IsHighSurrogate(character))
        {
            highSurrogate = character;
            return;
        }

        var text = char.IsLowSurrogate(character) ? (highSurrogate is char high ? $"{high}{character}" : null) : character.ToString();
        highSurrogate = null;
        if (text is not null) Press(new TvRemote.Text(text));
    }

    /// The PC keys that act like the TV remote's.
    internal static string? Key(VirtualKey key) => key switch
    {
        VirtualKey.Up => TvRemote.Up,
        VirtualKey.Down => TvRemote.Down,
        VirtualKey.Left => TvRemote.Left,
        VirtualKey.Right => TvRemote.Right,
        VirtualKey.Enter => TvRemote.Select,
        VirtualKey.Escape => TvRemote.Back,
        VirtualKey.Back => TvRemote.Delete,
        _ => null,
    };
}
