using FlyyLink.Icons;
using FlyyLink.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Views;

public enum RequestKind { Pairing, FlyyDrop }

/// Something waiting for the user's answer: a device asking to pair, or a FlyyDrop asking to be accepted.
public sealed record Request(RequestKind Kind, string Id);

/// Brings requests to the front one at a time, above other windows, so they are seen without opening the tray panel.
public sealed class RequestWindow : Window
{
    private const int CardWidth = 360;

    private readonly AppModel model;
    private readonly IntPtr hwnd;
    private readonly Grid root = new() { Padding = new Thickness(24, 40, 24, 24) };
    private readonly PairingPane pairing;
    private readonly FlyyDropPane flyyDrop;
    private Request? shown;
    private readonly HashSet<Request> dismissed = [];

    public RequestWindow(AppModel model)
    {
        this.model = model;
        Title = "FlyyLink";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(AppPaths.WindowIcon);
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            if (shown is not null) dismissed.Add(shown);
            Disappear();
        };

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        pairing = new PairingPane(DispatcherQueue, id => model.Accept(id), id => model.Reject(id));
        flyyDrop = new FlyyDropPane(id => model.AcceptFlyyDrop(id), id => model.DeclineFlyyDrop(id));
        root.Children.Add(pairing.View);
        root.Children.Add(flyyDrop.View);
        Content = root;

        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AppModel.Devices) or nameof(AppModel.FlyyDropAsks)) Update();
        };
    }

    /// Keeps showing the current request while it is pending, else shows the next one, and hides once none is left.
    private void Update()
    {
        var pending = model.Devices.Where(d => d.IsTheirRequest).Select(d => new Request(RequestKind.Pairing, d.Id))
            .Concat(model.FlyyDropAsks.Select(ask => new Request(RequestKind.FlyyDrop, ask.Id)))
            .ToList();

        dismissed.IntersectWith(pending);

        var next = pending.FirstOrDefault(r => r == shown) ?? pending.FirstOrDefault(r => !dismissed.Contains(r));
        if (next is null)
        {
            Disappear();
            return;
        }

        var appearing = next != shown || !AppWindow.IsVisible;
        shown = next;
        Show(next);

        if (appearing) Appear();
    }

    private void Show(Request request)
    {
        var isPairing = request.Kind == RequestKind.Pairing;
        pairing.View.Visibility = isPairing ? Visibility.Visible : Visibility.Collapsed;
        flyyDrop.View.Visibility = isPairing ? Visibility.Collapsed : Visibility.Visible;
        AppWindow.Title = isPairing ? "Pair" : "FlyyDrop";

        if (isPairing && model.Devices.FirstOrDefault(d => d.Id == request.Id) is { } device) pairing.Show(device);
        if (!isPairing && model.FlyyDropAsks.FirstOrDefault(a => a.Id == request.Id) is { } ask) flyyDrop.Show(ask);
    }

    /// Centres the window on the main screen, sized to its content, and brings it to the front.
    private void Appear()
    {
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var work = DisplayArea.Primary.WorkArea;

        root.Measure(new Size(CardWidth, double.PositiveInfinity));
        var width = (int)(CardWidth * scale);
        var height = (int)(root.DesiredSize.Height * scale);

        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        AppWindow.Show(true);
        Activate();
        SetForegroundWindow(hwnd);

        if (shown?.Kind == RequestKind.Pairing) pairing.Start();
        else flyyDrop.Start();
    }

    private void Disappear()
    {
        shown = null;
        pairing.Stop();
        flyyDrop.Stop();
        AppWindow.Hide();
    }

    internal static object Resource(string key) => Application.Current.Resources[key];

    internal static Grid Buttons(Button decline, Button accept)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(accept, 1);
        grid.Children.Add(decline);
        grid.Children.Add(accept);

        return grid;
    }

    internal static Button Pill(string text, bool prominent) =>
        new() { Content = text, Style = (Style)Resource(prominent ? "FlyyLinkPillAccentButton" : "FlyyLinkPillButton") };
}

/// The pairing prompt: both devices, the code to compare, and Not now or Pair.
internal sealed class PairingPane
{
    private readonly DispatcherQueueTimer pulse;
    private readonly DeviceArt art = new() { ArtWidth = 36 };
    private readonly Ellipse[] dots = [Dot(), Dot(), Dot()];
    private readonly TextBlock title = new() { FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock hint = new() { TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel code = new() { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button pair = RequestWindow.Pill("Pair", true);
    private string? deviceId;
    private int dot;

    public PairingPane(DispatcherQueue queue, Action<string> accept, Action<string> reject)
    {
        title.Style = (Style)RequestWindow.Resource("FlyyLinkTitle");
        hint.Style = (Style)RequestWindow.Resource("FlyyLinkBody");

        var notNow = RequestWindow.Pill("Not now", false);
        notNow.Click += (_, _) =>
        {
            if (deviceId is string id) reject(id);
        };
        pair.Click += (_, _) =>
        {
            if (deviceId is string id) accept(id);
        };

        var signal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        foreach (var each in dots) signal.Children.Add(each);

        View.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { art, signal, new LaptopArt() },
        });
        View.Children.Add(new StackPanel { Spacing = 6, Children = { title, hint } });
        View.Children.Add(code);
        View.Children.Add(RequestWindow.Buttons(notNow, pair));

        pulse = queue.CreateTimer();
        pulse.Interval = TimeSpan.FromMilliseconds(400);
        pulse.Tick += (_, _) => StepDots();
    }

    public StackPanel View { get; } = new() { Spacing = 18 };

    public void Show(DeviceState device)
    {
        deviceId = device.Id;
        art.Type = device.Type;
        title.Text = device.PairingTitle;
        hint.Text = device.PairingHint;

        code.Children.Clear();
        foreach (var character in device.CodeCharacters) code.Children.Add(Digit(character));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(code, $"Verification code {device.Code}");
    }

    public void Start()
    {
        pulse.Start();
        pair.Focus(FocusState.Programmatic);
    }

    public void Stop() => pulse.Stop();

    private void StepDots()
    {
        dot = (dot + 1) % dots.Length;
        for (var i = 0; i < dots.Length; i++) dots[i].Opacity = i == dot ? 1 : 0.3;
    }

    private static Ellipse Dot()
    {
        var dot = new Ellipse { Width = 6, Height = 6, Opacity = 0.3 };
        dot.SetValue(Shape.FillProperty, RequestWindow.Resource("FlyyLinkAccentBrush"));

        return dot;
    }

    private static Border Digit(string character)
    {
        var text = new TextBlock
        {
            Text = character,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetValue(TextBlock.ForegroundProperty, RequestWindow.Resource("FlyyLinkAccentStrongBrush"));

        var digit = new Border { Width = 28, Height = 40, CornerRadius = new CornerRadius(8), Child = text };
        digit.SetValue(Border.BackgroundProperty, RequestWindow.Resource("FlyyLinkTintBrush"));

        return digit;
    }
}

/// The FlyyDrop prompt: who is sending what, where it lands, and Decline or Accept.
internal sealed class FlyyDropPane
{
    private readonly Storyboard breathe = new() { RepeatBehavior = RepeatBehavior.Forever };
    private readonly DeviceArt art = new() { ArtWidth = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly LucideIcon icon = new() { Size = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock summary = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock hint = new() { FontSize = 10.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel hintRow;
    private readonly Button accept = RequestWindow.Pill("Accept", true);
    private string? askId;

    public FlyyDropPane(Action<string> onAccept, Action<string> onDecline)
    {
        title.Style = (Style)RequestWindow.Resource("FlyyLinkTitle");
        summary.SetValue(TextBlock.ForegroundProperty, RequestWindow.Resource("FlyyLinkTextBrush"));
        hint.SetValue(TextBlock.ForegroundProperty, RequestWindow.Resource("FlyyLinkSubBrush"));
        icon.SetValue(Control.ForegroundProperty, RequestWindow.Resource("FlyyLinkAccentBrush"));

        var decline = RequestWindow.Pill("Decline", false);
        decline.Click += (_, _) =>
        {
            if (askId is string id) onDecline(id);
        };
        accept.Click += (_, _) =>
        {
            if (askId is string id) onAccept(id);
        };

        var scale = new ScaleTransform();
        var halo = new Ellipse { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale };
        halo.SetValue(Shape.FillProperty, RequestWindow.Resource("FlyyLinkTintBrush"));
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var grow = new DoubleAnimation { From = 0.9, To = 1.05, Duration = new Duration(TimeSpan.FromSeconds(1.2)), AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, property);
            breathe.Children.Add(grow);
        }

        var tile = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Child = icon };
        tile.SetValue(Border.BackgroundProperty, RequestWindow.Resource("FlyyLinkTintBrush"));
        var item = new Grid { ColumnSpacing = 12, Padding = new Thickness(10), CornerRadius = new CornerRadius(12) };
        item.SetValue(Panel.BackgroundProperty, RequestWindow.Resource("FlyyLinkThumbBrush"));
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(summary, 1);
        item.Children.Add(tile);
        item.Children.Add(summary);

        var lockIcon = new LucideIcon { Icon = Lucide.Lock, Size = 11, VerticalAlignment = VerticalAlignment.Center };
        lockIcon.SetValue(Control.ForegroundProperty, RequestWindow.Resource("FlyyLinkSubBrush"));
        hintRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Children = { lockIcon, hint } };

        View.Children.Add(new Grid { Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Center, Children = { halo, art } });
        View.Children.Add(title);
        View.Children.Add(item);
        View.Children.Add(RequestWindow.Buttons(decline, accept));
        View.Children.Add(hintRow);
    }

    public StackPanel View { get; } = new() { Spacing = 16 };

    public void Show(FlyyDropAsk ask)
    {
        askId = ask.Id;
        art.Type = ask.DeviceType;
        title.Text = ask.Title;
        icon.Icon = ask.Icon;
        summary.Text = ask.Summary;
        hint.Text = ask.Hint;
        hintRow.Visibility = ask.Hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Start()
    {
        breathe.Begin();
        accept.Focus(FocusState.Programmatic);
    }

    public void Stop() => breathe.Stop();
}
