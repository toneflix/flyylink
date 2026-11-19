using FlyyLink.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Views;

/// Brings a pairing request to the front, above other windows, so it is seen without opening the tray panel.
public sealed class PairingWindow : Window
{
    private const int CardWidth = 360;

    private readonly AppModel model;
    private readonly IntPtr hwnd;
    private readonly DispatcherQueueTimer pulse;
    private readonly StackPanel root = new() { Spacing = 18, Padding = new Thickness(24, 40, 24, 24) };
    private readonly DeviceArt art = new() { ArtWidth = 36 };
    private readonly Ellipse[] dots = [Dot(), Dot(), Dot()];
    private readonly TextBlock title = new() { FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock hint = new() { TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel code = new() { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button pair = new() { Content = "Pair" };
    private string? deviceId;
    private string? dismissedId;
    private int dot;

    public PairingWindow(AppModel model)
    {
        this.model = model;
        Title = "Pair";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(AppPaths.WindowIcon);
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            dismissedId = deviceId;
            Disappear();
        };

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        title.Style = (Style)Resource("FlyyLinkTitle");
        hint.Style = (Style)Resource("FlyyLinkBody");
        pair.Style = (Style)Resource("FlyyLinkPillAccentButton");
        pair.Click += (_, _) =>
        {
            if (deviceId is string id) model.Accept(id);
        };

        var notNow = new Button { Content = "Not now", Style = (Style)Resource("FlyyLinkPillButton") };
        notNow.Click += (_, _) =>
        {
            if (deviceId is string id) model.Reject(id);
        };

        var signal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        foreach (var each in dots) signal.Children.Add(each);

        var buttons = new Grid { ColumnSpacing = 8 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(pair, 1);
        buttons.Children.Add(notNow);
        buttons.Children.Add(pair);

        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { art, signal, new LaptopArt() },
        });
        root.Children.Add(new StackPanel { Spacing = 6, Children = { title, hint } });
        root.Children.Add(code);
        root.Children.Add(buttons);
        Content = root;

        pulse = DispatcherQueue.CreateTimer();
        pulse.Interval = TimeSpan.FromMilliseconds(400);
        pulse.Tick += (_, _) => StepDots();

        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppModel.Devices)) Update();
        };
    }

    /// Shows the device asking to pair, and hides once no device is asking.
    private void Update()
    {
        var asking = model.Devices.Where(d => d.IsTheirRequest).ToList();

        if (dismissedId is not null && asking.All(d => d.Id != dismissedId)) dismissedId = null;

        var next = asking.FirstOrDefault(d => d.Id == deviceId) ?? asking.FirstOrDefault(d => d.Id != dismissedId);
        if (next is null)
        {
            Disappear();
            return;
        }

        var appearing = next.Id != deviceId || !AppWindow.IsVisible;
        deviceId = next.Id;
        Show(next);

        if (appearing) Appear();
    }

    private void Show(DeviceState device)
    {
        art.Type = device.Type;
        title.Text = device.PairingTitle;
        hint.Text = device.PairingHint;

        code.Children.Clear();
        foreach (var character in device.CodeCharacters) code.Children.Add(Digit(character));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(code, $"Verification code {device.Code}");
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
        pair.Focus(FocusState.Programmatic);
        pulse.Start();
    }

    private void Disappear()
    {
        deviceId = null;
        pulse.Stop();
        AppWindow.Hide();
    }

    private void StepDots()
    {
        dot = (dot + 1) % dots.Length;
        for (var i = 0; i < dots.Length; i++) dots[i].Opacity = i == dot ? 1 : 0.3;
    }

    private static Ellipse Dot()
    {
        var dot = new Ellipse { Width = 6, Height = 6, Opacity = 0.3 };
        dot.SetValue(Shape.FillProperty, Resource("FlyyLinkAccentBrush"));

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
        text.SetValue(TextBlock.ForegroundProperty, Resource("FlyyLinkAccentStrongBrush"));

        var digit = new Border { Width = 28, Height = 40, CornerRadius = new CornerRadius(8), Child = text };
        digit.SetValue(Border.BackgroundProperty, Resource("FlyyLinkTintBrush"));

        return digit;
    }

    private static object Resource(string key) => Application.Current.Resources[key];
}
