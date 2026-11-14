using FlyyLink.Core;
using FlyyLink.Icons;
using FlyyLink.Model;
using FlyyLink.Plugins;
using FlyyLink.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Views;

/// The panel that opens from the tray icon, like the Mac app's menu bar panel. It hides when it loses focus.
public sealed partial class PanelWindow : Window
{
    private const int Margin = 12;
    private const double PanelWidth = 360;

    private readonly IntPtr hwnd;
    private readonly DispatcherQueueTimer dots;
    private readonly DispatcherQueueTimer progress;
    private PhoneLocation? mapped;
    private DateTimeOffset hiddenAt = DateTimeOffset.MinValue;
    private RECT? anchor;
    private bool keepOpen;
    private bool quitting;
    private int dot;

    public PanelWindow(AppModel model)
    {
        Model = model;
        InitializeComponent();
        Model.PlayOnTv.KeepSearching();

        // Acrylic follows Windows' transparency setting and turns solid when it is off.
        SystemBackdrop = new DesktopAcrylicBackdrop();

        hwnd = WindowNative.GetWindowHandle(this);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(AppPaths.WindowIcon);
        AppWindow.Closing += (_, args) =>
        {
            if (quitting) return;

            args.Cancel = true;
            Hide();
        };

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);

        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && !keepOpen) Hide();
        };
        Sections.SizeChanged += (_, _) => Fit();

        dots = DispatcherQueue.CreateTimer();
        dots.Interval = TimeSpan.FromMilliseconds(400);
        dots.Tick += (_, _) => StepDots();

        progress = DispatcherQueue.CreateTimer();
        progress.Interval = TimeSpan.FromSeconds(1);
        progress.Tick += (_, _) => ShowProgress();

        Model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppModel.Focused)) ShowProgress();
            if (args.PropertyName is nameof(AppModel.Focused) or nameof(AppModel.InputStatus)) ShowEdges();
            if (args.PropertyName == nameof(AppModel.Found)) ShowMap(Model.Found);
        };

        StartPulse();
    }

    public AppModel Model { get; }

    public bool NotificationsMissing => !Model.NotificationsAvailable;

    /// Quit was picked in the panel.
    public event Action? QuitRequested;

    public bool IsShown => AppWindow.IsVisible;

    /// Opens or closes the panel from the tray icon. A click that just closed it by taking focus away does not reopen it.
    internal void Toggle(RECT? tray)
    {
        if (IsShown)
        {
            Hide();
            return;
        }

        if (DateTimeOffset.Now - hiddenAt < TimeSpan.FromMilliseconds(300)) return;

        ShowNear(tray);
    }

    internal void ShowNear(RECT? tray)
    {
        anchor = tray;
        Fit();
        AppWindow.Show(true);
        Activate();
        SetForegroundWindow(hwnd);
        dots.Start();
        progress.Start();
        ShowProgress();
    }

    public void Hide()
    {
        if (!IsShown) return;

        AppWindow.Hide();
        dots.Stop();
        progress.Stop();
        hiddenAt = DateTimeOffset.Now;
    }

    /// Closes the window for good, when FlyyLink quits.
    public void Shutdown()
    {
        quitting = true;
        dots.Stop();
        progress.Stop();
        Close();
    }

    // Size and place

    /// Sizes the window to its content and places it by the tray icon, inside the screen's work area.
    private void Fit()
    {
        var scale = GetDpiForWindow(hwnd) / 96.0;
        Sections.Measure(new Windows.Foundation.Size(PanelWidth, double.PositiveInfinity));

        var point = anchor is RECT icon ? new POINT { X = (icon.Left + icon.Right) / 2, Y = (icon.Top + icon.Bottom) / 2 } : Cursor();
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), ref info);
        var work = info.rcWork;
        var screen = info.rcMonitor;
        var margin = (int)(Margin * scale);

        // The content area gets the panel's size, and the border goes around it.
        AppWindow.ResizeClient(new SizeInt32(
            (int)Math.Ceiling(PanelWidth * scale),
            Math.Min((int)Math.Ceiling(Sections.DesiredSize.Height * scale), work.Bottom - work.Top - 2 * margin)));
        var width = AppWindow.Size.Width;
        var height = Math.Min(AppWindow.Size.Height, work.Bottom - work.Top - 2 * margin);

        // The taskbar sits on the side where the work area stops short of the screen.
        int x, y;
        if (work.Left > screen.Left)
        {
            x = work.Left + margin;
            y = Math.Clamp(point.Y - height / 2, work.Top + margin, work.Bottom - height - margin);
        }
        else if (work.Right < screen.Right)
        {
            x = work.Right - width - margin;
            y = Math.Clamp(point.Y - height / 2, work.Top + margin, work.Bottom - height - margin);
        }
        else if (work.Top > screen.Top)
        {
            x = Math.Clamp(point.X - width / 2, work.Left + margin, work.Right - width - margin);
            y = work.Top + margin;
        }
        else
        {
            x = Math.Clamp(point.X - width / 2, work.Left + margin, work.Right - width - margin);
            y = work.Bottom - height - margin;
        }

        AppWindow.Move(new PointInt32(x, y));
    }

    private static POINT Cursor()
    {
        GetCursorPos(out var point);

        return point;
    }

    // Animations

    private void StartPulse()
    {
        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        void Ring(DependencyObject scale, UIElement ring, double delay)
        {
            var begin = TimeSpan.FromSeconds(delay);
            var duration = new Duration(TimeSpan.FromSeconds(2));

            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var grow = new DoubleAnimation { From = 1, To = 1.9, Duration = duration, BeginTime = begin, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(grow, scale);
                Storyboard.SetTargetProperty(grow, property);
                storyboard.Children.Add(grow);
            }

            var fade = new DoubleAnimation { From = 1, To = 0, Duration = duration, BeginTime = begin };
            Storyboard.SetTarget(fade, ring);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
        }

        Ring(PulseOneScale, PulseOne, 0);
        Ring(PulseTwoScale, PulseTwo, 1);
        storyboard.Begin();

        // The FlyyDrop request's device breathes gently while it waits for an answer.
        var breathe = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var grow = new DoubleAnimation { From = 0.9, To = 1.05, Duration = new Duration(TimeSpan.FromSeconds(1.2)), AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(grow, FlyyDropPulseScale);
            Storyboard.SetTargetProperty(grow, property);
            breathe.Children.Add(grow);
        }
        breathe.Begin();
    }

    private void StepDots()
    {
        dot = (dot + 1) % 3;
        DotOne.Opacity = dot == 0 ? 1 : 0.3;
        DotTwo.Opacity = dot == 1 ? 1 : 0.3;
        DotThree.Opacity = dot == 2 ? 1 : 0.3;
    }

    // Bound helpers

    public string SearchingTitle(bool hasNearby) => hasNearby ? "Pick your phone to pair" : "Looking for your phone";

    public string SearchingHint(bool hasNearby) => hasNearby ? "" : "Open FlyyLink on your phone, on the same Wi-Fi.";

    public int BatteryCharge(DeviceState? device) => device?.Battery?.Charge ?? -1;

    public Core.DeviceType FocusedType(DeviceState? device) => device?.Type ?? Core.DeviceType.Phone;

    public double HeroArtWidth(DeviceState? device) => device?.IsComputer == true ? 30 : 40;

    /// Desktops have no battery, so their card leaves the ring out.
    public Visibility ShowsBattery(DeviceState? device) => Shown(device is { IsPhone: true } || device?.Battery is not null);

    public Brush InputChip(bool active) => (Brush)Application.Current.Resources[active ? "FlyyLinkAccentBrush" : "FlyyLinkTintBrush"];

    public Brush InputGlyph(bool active) => active ? new SolidColorBrush(Microsoft.UI.Colors.White) : (Brush)Application.Current.Resources["FlyyLinkAccentBrush"];

    private static readonly (ScreenEdge? Edge, string Title)[] Placements =
        [(null, "Off"), (ScreenEdge.Left, "Left"), (ScreenEdge.Right, "Right"), (ScreenEdge.Top, "Above"), (ScreenEdge.Bottom, "Below")];

    /// The segmented row that picks which side of this PC the shown computer sits on.
    private void ShowEdges()
    {
        EdgePicker.Children.Clear();
        EdgePicker.ColumnDefinitions.Clear();
        if (Model.Focused is not { IsComputer: true, IsPaired: true } device) return;

        var current = Model.InputEdge(device.Id);
        for (var index = 0; index < Placements.Length; index++)
        {
            var (edge, title) = Placements[index];
            var button = new ToggleButton
            {
                Content = title,
                IsChecked = edge == current,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0, 5, 0, 5),
                CornerRadius = new CornerRadius(8),
                FontSize = 12,
            };
            button.Click += (_, _) =>
            {
                Model.SetInputEdge(edge, device.Id);
                ShowEdges();
            };

            EdgePicker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(button, index);
            EdgePicker.Children.Add(button);
        }
    }

    public Lucide NotificationsIcon(bool on) => on ? Lucide.BellRing : Lucide.BellOff;

    // The hero's notifications badge: a bell, crossed out and faded while paused.
    public Lucide HeroBell(bool on) => on ? Lucide.Bell : Lucide.BellOff;

    public string NotificationsHint(bool on) => on ? "Pause notifications" : "Turn on notifications";

    public Brush StatusBackground(bool on) => new SolidColorBrush(global::Windows.UI.Color.FromArgb(on ? (byte)0x14 : (byte)0x0A, 0, 0, 0));

    public double StatusOpacity(bool on) => on ? 1 : 0.55;

    /// The carousel runs to the window's edge so the next card looks cut off by it.
    public Thickness CarouselBleed(bool carousel) => carousel ? new Thickness(0, 0, -14, 0) : new Thickness(0);

    public string NextTitle(DeviceState? next) => next is null ? "" : $"Show {next.Name}";

    public Visibility HasMedia(DeviceState? device) => Shown(device?.Media is not null);

    public Visibility HasLength(DeviceState? device) => Shown(device?.Media?.Player.Length > 0);

    public string MediaTitle(DeviceState? device) => device?.Media?.Player.Title ?? "";

    public string MediaSubtitle(DeviceState? device) =>
        device?.Media?.Player is { } player ? string.Join(" · ", new[] { player.Artist, player.Name }.Where(s => s.Length > 0)) : "";

    public ImageSource? Artwork(DeviceState? device) => Pictures.FromFile(device?.Media?.Artwork, 92);

    public Lucide PlayIcon(DeviceState? device) => device?.Media?.Player.IsPlaying == true ? Lucide.Pause : Lucide.Play;

    public bool CanGoPrevious(DeviceState? device) => device?.Media?.Player.CanGoPrevious == true;

    public bool CanGoNext(DeviceState? device) => device?.Media?.Player.CanGoNext == true;

    public Visibility IsFinding(string? finding, DeviceState? device) => Shown(finding is not null && finding == device?.Id);

    public string FindTitle(DeviceState? device) => device is null ? "" : $"Finding {device.Name}";

    public Visibility HasLocation(PhoneLocation? location) => Shown(location is not null);

    public Visibility NoLocation(PhoneLocation? location) => Shown(location is null);

    public bool IsLocating(PhoneLocation? location) => location is null;

    // Function bindings can't turn a bool into Visibility themselves, so these return it directly.
    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility Hidden(bool hidden) => Shown(!hidden);

    public string LocationDetail(PhoneLocation? location)
    {
        if (location is null) return "";

        var when = location.Time > 0 ? RelativeTime.Text(DateTimeOffset.FromUnixTimeMilliseconds(location.Time)) : "just now";

        return $"Within {Math.Round(location.Accuracy)} m · {when}";
    }

    public static Lucide RowIcon(Core.DeviceType type) => type switch
    {
        Core.DeviceType.Laptop => Lucide.Laptop,
        Core.DeviceType.Desktop => Lucide.Monitor,
        _ => Lucide.Smartphone,
    };

    public Core.DeviceType AskType(FlyyDropAsk? ask) => ask?.DeviceType ?? Core.DeviceType.Phone;

    public Lucide AskIcon(FlyyDropAsk? ask) => ask?.Icon ?? Lucide.File;

    public static string RowAction(PairState state) => state switch
    {
        PairState.Paired => "Show",
        PairState.Requested => "Waiting",
        PairState.RequestedByPeer => "Review",
        _ => "Pair",
    };

    // Actions

    private string? FocusedId => Model.Focused?.Id;

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (FocusedId is string id) Model.Accept(id);
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (FocusedId is string id) Model.Reject(id);
    }

    private void ViewScreen_Click(object sender, RoutedEventArgs e)
    {
        if (FocusedId is string id) Model.StartMirroring(id);
    }

    private void StopScreenShare_Click(object sender, RoutedEventArgs e) => Model.StopScreenShare();

    private void Ring_Click(object sender, RoutedEventArgs e)
    {
        if (FocusedId is string id) Model.Ring(id);
    }

    private void Clipboard_Click(object sender, RoutedEventArgs e)
    {
        if (FocusedId is string id) Model.SendClipboard(id);
    }

    /// Opens the copied link on the TV the panel shows.
    private async void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (FocusedId is not string id) return;

        var content = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (!content.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;

        Model.SendText(await content.GetTextAsync(), id);
    }

    private void Notifications_Click(object sender, RoutedEventArgs e) => Model.MirrorsNotifications = !Model.MirrorsNotifications;

    private void NextPhone_Click(object sender, RoutedEventArgs e)
    {
        Model.ShowNextPhone();
        SlideIn();
    }

    /// The new phone's card slides in from the right, where it was peeking.
    private void SlideIn()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(320));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var shift = new DoubleAnimation { From = 60, To = 0, Duration = duration, EasingFunction = ease };
        var fade = new DoubleAnimation { From = 0.4, To = 1, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(shift, HeroShift);
        Storyboard.SetTargetProperty(shift, "X");
        Storyboard.SetTarget(fade, Hero);
        Storyboard.SetTargetProperty(fade, "Opacity");

        new Storyboard { Children = { shift, fade } }.Begin();
    }

    private void Messages_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        Model.ShowMessages();
    }

    private void Call_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        Model.ShowDialer();
    }

    private void Mirror_Click(object sender, RoutedEventArgs e)
    {
        if (FocusedId is not string id) return;

        Hide();
        Model.StartMirroring(id);
    }

    private void Photos_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        Model.ShowPhotos();
    }

    private void JoinHotspot_Click(object sender, RoutedEventArgs e) => Model.JoinPhoneHotspot();

    private void CancelHotspot_Click(object sender, RoutedEventArgs e) => Model.CancelHotspot();

    private void StopFinding_Click(object sender, RoutedEventArgs e) => Model.StopFinding();

    private void OpenMap_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Found is PhoneLocation location) Explorer.Open(MapLinks.Search(location));
    }

    private void Media_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<MediaAction>(tag, out var action)) return;
        if (Model.Focused is not { Media: { } media } device) return;

        Model.Control(device.Id, media.Player.Name, action);
    }

    private void MediaTrack_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Model.Focused is not { Media: { Player: { CanSeek: true, Length: > 0 } player } } device || MediaTrack.ActualWidth <= 0) return;

        var fraction = Math.Clamp(e.GetCurrentPoint(MediaTrack).Position.X / MediaTrack.ActualWidth, 0, 1);
        Model.Seek(device.Id, player.Name, (long)(fraction * player.Length));
        MediaProgress.Value = fraction;
    }

    /// Moves the song's progress bar along while the panel is open.
    private void ShowProgress()
    {
        if (Model.Focused?.Media is not NowPlaying media || media.Player.Length <= 0) return;

        MediaProgress.Value = (double)media.Position(DateTimeOffset.Now) / media.Player.Length;
    }

    private void ShowMap(PhoneLocation? location)
    {
        if (location is null || location == mapped) return;

        mapped = location;
        Map.Source = new Uri(MapLinks.Embed(location));
    }

    private void SendFiles_Click(object sender, RoutedEventArgs e)
    {
        if (FocusedId is string id) ChooseFiles(id);
    }

    /// Lets the user pick files and sends them to phone `id`.
    internal async void ChooseFiles(string id)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, CommitButtonText = "Send" };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hwnd);

        keepOpen = true;
        try
        {
            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 }) SendFiles(files.Select(f => f.Path), id);
        }
        finally
        {
            keepOpen = false;
        }
    }

    /// Sends to a paired device, or asks an unpaired one with FlyyDrop.
    private void SendFiles(IEnumerable<string> paths, string id)
    {
        if (Model.Devices.FirstOrDefault(d => d.Id == id) is { ShowsFlyyDrop: true }) Model.FlyyDropFiles(paths, id);
        else Model.SendFiles(paths, id);
    }

    private void SendText(string text, string id)
    {
        if (Model.Devices.FirstOrDefault(d => d.Id == id) is { ShowsFlyyDrop: true }) Model.FlyyDropText(text, id);
        else Model.SendText(text, id);
    }

    // Play on TV

    private async void PlayOnTv_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, CommitButtonText = "Choose" };
        foreach (var type in PlayOnTvTypes) picker.FileTypeFilter.Add(type);
        InitializeWithWindow.Initialize(picker, hwnd);

        keepOpen = true;
        try
        {
            if (await picker.PickSingleFileAsync() is StorageFile file) Model.PlayOnTv.Pick(file.Path);
        }
        finally
        {
            keepOpen = false;
        }
    }

    private static readonly string[] PlayOnTvTypes =
        [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg"];

    private void SearchTvs_Click(object sender, RoutedEventArgs e) => Model.PlayOnTv.Search();

    private void CancelTvPick_Click(object sender, RoutedEventArgs e) => Model.PlayOnTv.CancelPick();

    private void PlayOnTvTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && Model.PlayOnTv.Tvs.FirstOrDefault(tv => tv.Id == id) is TvRenderer tv) Model.PlayOnTv.Play(tv);
    }

    private void TvPlayPause_Click(object sender, RoutedEventArgs e) => Model.PlayOnTv.TogglePause();

    private void TvStop_Click(object sender, RoutedEventArgs e) => Model.PlayOnTv.Stop();

    private void TvTrack_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (TvTrack.ActualWidth <= 0) return;

        Model.PlayOnTv.Seek(e.GetCurrentPoint(TvTrack).Position.X / TvTrack.ActualWidth);
    }

    // FlyyDrop

    private void AcceptFlyyDrop_Click(object sender, RoutedEventArgs e)
    {
        if (Model.FlyyDropAsk is { } ask) Model.AcceptFlyyDrop(ask.Id);
    }

    private void DeclineFlyyDrop_Click(object sender, RoutedEventArgs e)
    {
        if (Model.FlyyDropAsk is { } ask) Model.DeclineFlyyDrop(ask.Id);
    }

    private void StopFlyyDropReceiving_Click(object sender, RoutedEventArgs e) => Model.FlyyDropReceiving = Core.FlyyDrop.Receiving.Off;

    private void FlyyDrop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Model.Devices.FirstOrDefault(d => d.Id == id) is not DeviceState device) return;

        if (device.IsFlyyDropWaiting) Model.CancelFlyyDrop(id);
        else ChooseFiles(id);
    }

    /// The device a nearby row stands for, when files can go to it.
    private DeviceState? DropTarget(object row) =>
        row is FrameworkElement { Tag: string id } && Model.Devices.FirstOrDefault(d => d.Id == id) is { } device && (device.IsPaired || device.ShowsFlyyDrop) ? device : null;

    private void Row_DragOver(object sender, DragEventArgs e)
    {
        if (DropTarget(sender) is not DeviceState device || sender is not Grid row) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Text) && !e.DataView.Contains(StandardDataFormats.WebLink)) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = device.IsPaired ? $"Send to {device.Name}" : $"FlyyDrop to {device.Name}";
        row.Background = (Brush)Application.Current.Resources["FlyyLinkTintBrush"];
    }

    private void Row_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Grid row) row.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private async void Row_Drop(object sender, DragEventArgs e)
    {
        Row_DragLeave(sender, e);
        if (DropTarget(sender) is not DeviceState device) return;

        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                SendFiles(items.OfType<StorageFile>().Select(f => f.Path), device.Id);
            }
            else if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                SendText((await e.DataView.GetWebLinkAsync()).AbsoluteUri, device.Id);
            }
            else if (e.DataView.Contains(StandardDataFormats.Text))
            {
                SendText(await e.DataView.GetTextAsync(), device.Id);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void RowAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } || Model.Devices.FirstOrDefault(d => d.Id == id) is not DeviceState device) return;

        switch (device.PairState)
        {
            case PairState.NotPaired:
                Model.RequestPair(id);
                break;
            case PairState.Paired:
                Model.Select(id);
                break;
        }
    }

    private void Activity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Activity activity }) return;

        if (activity.Path is string path) Explorer.OpenFile(path);
        else if (activity.Url is string url) Explorer.Open(url);
        else Expand(sender);
    }

    /// Shows a Recent row's full title and detail, or folds it back to one line each.
    private static void Expand(object row)
    {
        if (row is not Button { Content: Grid content } || content.Children.OfType<StackPanel>().FirstOrDefault() is not StackPanel text) return;

        var lines = text.Children.OfType<TextBlock>().ToList();
        var expanding = lines.FirstOrDefault()?.TextWrapping != TextWrapping.Wrap;
        foreach (var line in lines)
        {
            line.TextWrapping = expanding ? TextWrapping.Wrap : TextWrapping.NoWrap;
            line.TextTrimming = expanding ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        }
    }

    /// Right-clicking a received file offers to show it in its folder.
    private void Activity_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button { Tag: Activity { Path: string path } } button) return;

        var open = new MenuFlyoutItem { Text = "Open" };
        open.Click += (_, _) => Explorer.OpenFile(path);
        var reveal = new MenuFlyoutItem { Text = "Show in folder" };
        reveal.Click += (_, _) => Explorer.Reveal(path);

        var menu = new MenuFlyout();
        menu.Items.Add(open);
        menu.Items.Add(reveal);

        if (e.TryGetPosition(button, out var point)) menu.ShowAt(button, point);
        else menu.ShowAt(button);
        e.Handled = true;
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e) => Model.ClearRecent();

    private void Quit_Click(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();

    private void LicenceSignIn_Click(object sender, RoutedEventArgs e) => Model.Licence.OpenCard();

    private void LicenceClose_Click(object sender, RoutedEventArgs e) => Model.Licence.CloseCard();

    private void LicenceBuy_Click(object sender, RoutedEventArgs e) => LicenceModel.Buy();

    private void LicenceAccount_Click(object sender, RoutedEventArgs e) => LicenceModel.ManageComputers();

    private void LicenceRetry_Click(object sender, RoutedEventArgs e) => Model.Licence.Retry();

    private void LicenceSendCode_Click(object sender, RoutedEventArgs e) => Model.Licence.SendCode();

    private void LicenceActivate_Click(object sender, RoutedEventArgs e) => Model.Licence.Activate();

    private void LicenceOtherEmail_Click(object sender, RoutedEventArgs e) => Model.Licence.UseAnotherEmail();

    private void LicenceEmail_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && Model.Licence.IsIdle) Model.Licence.SendCode();
    }

    private void LicenceCode_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && Model.Licence.IsIdle) Model.Licence.Activate();
    }

    private void SettingsMenu_Opening(object sender, object e)
    {
        SettingsMenu.Items.Clear();

        var mirror = new ToggleMenuFlyoutItem { Text = "Show phone notifications", IsChecked = Model.MirrorsNotifications };
        mirror.Click += (_, _) => Model.MirrorsNotifications = mirror.IsChecked;
        SettingsMenu.Items.Add(mirror);

        if (Model.CanShareNotifications)
        {
            var share = new ToggleMenuFlyoutItem { Text = "Share this PC's notifications", IsChecked = Model.SharesNotifications };
            share.Click += (_, _) => Model.SharesNotifications = share.IsChecked;
            SettingsMenu.Items.Add(share);
        }

        var remote = new ToggleMenuFlyoutItem { Text = "Let my computers control this PC", IsChecked = Model.AllowsRemoteInput };
        remote.Click += (_, _) => Model.AllowsRemoteInput = remote.IsChecked;
        SettingsMenu.Items.Add(remote);

        var relay = new ToggleMenuFlyoutItem { Text = "Share phone notifications and calls with my computers", IsChecked = Model.RelaysPhones };
        relay.Click += (_, _) => Model.RelaysPhones = relay.IsChecked;
        SettingsMenu.Items.Add(relay);

        var startup = new ToggleMenuFlyoutItem { Text = "Start with Windows", IsChecked = Model.StartsWithWindows };
        startup.Click += (_, _) => Model.StartsWithWindows = startup.IsChecked;
        SettingsMenu.Items.Add(startup);

        var shortcuts = new MenuFlyoutSubItem { Text = "Keyboard shortcuts" };
        var useShortcuts = new ToggleMenuFlyoutItem { Text = "Use shortcuts", IsChecked = Model.UsesShortcuts };
        useShortcuts.Click += (_, _) => Model.UsesShortcuts = useShortcuts.IsChecked;
        shortcuts.Items.Add(useShortcuts);
        shortcuts.Items.Add(new MenuFlyoutSeparator());
        foreach (var shortcut in GlobalShortcuts.All) shortcuts.Items.Add(new MenuFlyoutItem { Text = $"{shortcut.Name}   {shortcut.Title}", IsEnabled = false });
        SettingsMenu.Items.Add(shortcuts);

        var photosFolder = new ToggleMenuFlyoutItem { Text = "Show phone photos in Explorer", IsChecked = Model.ShowsPhotosInExplorer };
        photosFolder.Click += (_, _) => Model.ShowsPhotosInExplorer = photosFolder.IsChecked;
        SettingsMenu.Items.Add(photosFolder);

        var switcher = new MenuFlyoutSubItem { Text = "Switch devices with" };
        foreach (var (style, label) in new[] { (PhoneSwitcherStyle.Carousel, "Card carousel"), (PhoneSwitcherStyle.Menu, "Dropdown") })
        {
            var item = new ToggleMenuFlyoutItem { Text = label, IsChecked = Model.SwitcherStyle == style };
            item.Click += (_, _) => Model.SwitcherStyle = style;
            switcher.Items.Add(item);
        }
        SettingsMenu.Items.Add(switcher);

        var flyyDrop = new MenuFlyoutSubItem { Text = "Who can FlyyDrop to this PC" };
        foreach (var (mode, label) in new[]
        {
            (Core.FlyyDrop.Receiving.Off, "Paired devices only"),
            (Core.FlyyDrop.Receiving.EveryoneFor10Minutes, "Everyone for 10 minutes"),
            (Core.FlyyDrop.Receiving.Everyone, "Everyone"),
        })
        {
            var item = new ToggleMenuFlyoutItem { Text = label, IsChecked = Model.FlyyDropReceiving == mode };
            item.Click += (_, _) => Model.FlyyDropReceiving = mode;
            flyyDrop.Items.Add(item);
        }
        SettingsMenu.Items.Add(flyyDrop);

        if (Model.Licence.LicensedTo is string owner)
        {
            var account = new MenuFlyoutItem { Text = $"Licensed to {owner}" };
            account.Click += (_, _) => LicenceModel.ManageComputers();
            SettingsMenu.Items.Add(account);
        }
        else if (Model.Licence.CanSignIn)
        {
            var signIn = new MenuFlyoutItem { Text = "Sign in to your licence" };
            signIn.Click += (_, _) => Model.Licence.OpenCard();
            SettingsMenu.Items.Add(signIn);
        }

        var updates = new MenuFlyoutItem { Text = $"Check for updates (you have {Model.VersionText})" };
        updates.Click += (_, _) => Model.CheckForUpdates();
        SettingsMenu.Items.Add(updates);

        var downloads = new MenuFlyoutItem { Text = "Open Downloads" };
        downloads.Click += (_, _) => Explorer.Open(AppPaths.Downloads);
        SettingsMenu.Items.Add(downloads);

        var paired = Model.Devices.Where(d => d.IsPaired).ToList();
        if (paired.Count > 0)
        {
            SettingsMenu.Items.Add(new MenuFlyoutSeparator());
            foreach (var device in paired)
            {
                var unpair = new MenuFlyoutItem { Text = $"Unpair {device.Name}" };
                unpair.Click += (_, _) => Model.Unpair(device.Id);
                SettingsMenu.Items.Add(unpair);
            }
        }

        SettingsMenu.Items.Add(new MenuFlyoutSeparator());
        var quit = new MenuFlyoutItem { Text = "Quit FlyyLink" };
        quit.Click += (_, _) => QuitRequested?.Invoke();
        SettingsMenu.Items.Add(quit);
    }

    private void PhoneMenu_Opening(object sender, object e)
    {
        PhoneMenu.Items.Clear();

        foreach (var phone in Model.OtherPhones)
        {
            var item = new MenuFlyoutItem { Text = phone.Name };
            item.Click += (_, _) => Model.Select(phone.Id);
            PhoneMenu.Items.Add(item);
        }
    }

    // Drag and drop

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Text) && !e.DataView.Contains(StandardDataFormats.WebLink)) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = $"Send to {Model.Focused?.Name ?? "phone"}";
        DropOutline.SetValue(Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty, Application.Current.Resources["FlyyLinkAccentBrush"]);
        DropLabel.Text = "Release to send";
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => ResetDropZone();

    private void DropZone_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (FocusedId is string id) ChooseFiles(id);
    }

    private void DropZone_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        DropOutline.SetValue(Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty, Application.Current.Resources["FlyyLinkAccentBrush"]);

    private void DropZone_PointerExited(object sender, PointerRoutedEventArgs e) => ResetDropZone();

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        ResetDropZone();
        if (FocusedId is not string id) return;

        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                Model.SendFiles(items.OfType<StorageFile>().Select(f => f.Path), id);
            }
            else if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                Model.SendText((await e.DataView.GetWebLinkAsync()).AbsoluteUri, id);
            }
            else if (e.DataView.Contains(StandardDataFormats.Text))
            {
                Model.SendText(await e.DataView.GetTextAsync(), id);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ResetDropZone()
    {
        DropOutline.SetValue(Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty, Application.Current.Resources["FlyyLinkDashBrush"]);
        DropLabel.Text = "Drop files or links to send";
    }
}
