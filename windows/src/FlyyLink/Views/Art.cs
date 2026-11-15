using FlyyLink.Icons;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace FlyyLink.Views;

/// Parses XAML path markup into a fresh geometry, since one geometry can't be shared between shapes.
internal static class Geometries
{
    public static Geometry Parse(string data) => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    public static Color Argb(uint value) => Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
}

/// A Lucide icon, stroked in the foreground colour.
public sealed class LucideIcon : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Lucide), typeof(LucideIcon), new PropertyMetadata(Lucide.File, (d, _) => ((LucideIcon)d).Draw()));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(LucideIcon), new PropertyMetadata(16.0, (d, _) => ((LucideIcon)d).Resize()));

    private readonly Microsoft.UI.Xaml.Shapes.Path path = new()
    {
        StrokeThickness = 2,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    public LucideIcon()
    {
        IsTabStop = false;
        path.SetBinding(Shape.StrokeProperty, new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        Content = new Viewbox { Child = canvas };

        Resize();
        Draw();
    }

    public Lucide Icon
    {
        get => (Lucide)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void Draw()
    {
        path.Data = Geometries.Parse(Icon.Data());

        if (Icon.IsSolid()) path.SetBinding(Shape.FillProperty, new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        else path.Fill = null;
    }

    private void Resize()
    {
        Width = Size;
        Height = Size;
    }
}

/// The app icon in miniature: the logo mark on its gold tile.
public sealed class AppGlyph : UserControl
{
    public AppGlyph() : this(26)
    {
    }

    public AppGlyph(double size)
    {
        var mark = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = Geometries.Parse(FlyyLinkMark.Data),
            Fill = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(FlyyLinkMark.StartX, FlyyLinkMark.StartY),
                EndPoint = new Point(FlyyLinkMark.EndX, FlyyLinkMark.EndY),
                GradientStops =
                {
                    new GradientStop { Color = Geometries.Argb(FlyyLinkMark.StartColor), Offset = 0 },
                    new GradientStop { Color = Geometries.Argb(FlyyLinkMark.EndColor), Offset = 1 },
                },
            },
        };
        var canvas = new Canvas { Width = 100, Height = 100 };
        canvas.Children.Add(mark);

        Content = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.26),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = Geometries.Argb(FlyyLinkMark.TileTop), Offset = 0 },
                    new GradientStop { Color = Geometries.Argb(FlyyLinkMark.TileBottom), Offset = 1 },
                },
            },
            Child = new Viewbox { Width = size * 0.66, Height = size * 0.66, Child = canvas },
        };
    }
}

/// A drawn phone: dark body with an accent screen. Height follows the width.
public sealed class PhoneArt : UserControl
{
    public static readonly DependencyProperty ArtWidthProperty =
        DependencyProperty.Register(nameof(ArtWidth), typeof(double), typeof(PhoneArt), new PropertyMetadata(40.0, (d, _) => ((PhoneArt)d).Draw()));

    public PhoneArt() => Draw();

    public double ArtWidth
    {
        get => (double)GetValue(ArtWidthProperty);
        set => SetValue(ArtWidthProperty, value);
    }

    private void Draw()
    {
        var width = ArtWidth;
        var bezel = Math.Max(2, width * 0.07);

        var screen = new Border { CornerRadius = new CornerRadius(width * 0.25 - bezel) };
        screen.SetValue(Border.BackgroundProperty, Application.Current.Resources["FlyyLinkAccentBrush"]);

        var body = new Border
        {
            Width = width,
            Height = width * 1.72,
            CornerRadius = new CornerRadius(width * 0.25),
            Padding = new Thickness(bezel),
            Child = screen,
        };
        body.SetValue(Border.BackgroundProperty, Application.Current.Resources["FlyyLinkDeviceBrush"]);

        Content = body;
    }
}

/// A drawn laptop: screen on a thin base.
public sealed class LaptopArt : UserControl
{
    public LaptopArt() => Content = ComputerDrawing.Laptop(76);
}

/// Draws laptops and desktops at a given width.
internal static class ComputerDrawing
{
    public static UIElement Laptop(double width)
    {
        var lid = Frame(width, width * 0.65, width * 0.075);
        var stand = new Border
        {
            Width = width * 1.15,
            Height = Math.Max(2, width * 0.075),
            CornerRadius = new CornerRadius(width * 0.04),
            Background = new SolidColorBrush(Color.FromArgb(0x73, 0x80, 0x80, 0x88)),
        };

        return new StackPanel { Children = { lid, stand }, VerticalAlignment = VerticalAlignment.Center };
    }

    /// A monitor on a short stand.
    public static UIElement Desktop(double width)
    {
        var monitor = Frame(width, width * 0.62, width * 0.06);
        var neck = new Border { Width = width * 0.12, Height = width * 0.12 };
        neck.SetValue(Border.BackgroundProperty, Application.Current.Resources["FlyyLinkDeviceBrush"]);
        var foot = new Border
        {
            Width = width * 0.42,
            Height = Math.Max(2, width * 0.06),
            CornerRadius = new CornerRadius(width * 0.03),
            Background = new SolidColorBrush(Color.FromArgb(0x73, 0x80, 0x80, 0x88)),
        };

        return new StackPanel { Children = { monitor, neck, foot }, VerticalAlignment = VerticalAlignment.Center };
    }

    /// A wide screen on a short stand.
    public static UIElement Tv(double width)
    {
        var screen = Frame(width, width * 0.58, width * 0.04);
        var neck = new Border { Width = width * 0.08, Height = width * 0.06 };
        neck.SetValue(Border.BackgroundProperty, Application.Current.Resources["FlyyLinkDeviceBrush"]);
        var foot = new Border
        {
            Width = width * 0.36,
            Height = Math.Max(2, width * 0.03),
            CornerRadius = new CornerRadius(width * 0.015),
            Background = new SolidColorBrush(Color.FromArgb(0x73, 0x80, 0x80, 0x88)),
        };

        return new StackPanel { Children = { screen, neck, foot }, VerticalAlignment = VerticalAlignment.Center };
    }

    private static Border Frame(double width, double height, double radius)
    {
        var screen = new Border { CornerRadius = new CornerRadius(width * 0.04) };
        screen.SetValue(Border.BackgroundProperty, Application.Current.Resources["FlyyLinkScreenBrush"]);

        var frame = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius), Padding = new Thickness(Math.Max(2, width * 0.04)), Child = screen };
        frame.SetValue(Border.BackgroundProperty, Application.Current.Resources["FlyyLinkDeviceBrush"]);

        return frame;
    }
}

/// The drawing for a kind of device. ArtWidth is a phone's width; computers are drawn wider to look the same size.
public sealed class DeviceArt : UserControl
{
    public static readonly DependencyProperty ArtWidthProperty =
        DependencyProperty.Register(nameof(ArtWidth), typeof(double), typeof(DeviceArt), new PropertyMetadata(40.0, (d, _) => ((DeviceArt)d).Draw()));

    public DeviceArt() => Draw();

    private Core.DeviceType type = Core.DeviceType.Phone;

    public Core.DeviceType Type
    {
        get => type;
        set
        {
            type = value;
            Draw();
        }
    }

    public double ArtWidth
    {
        get => (double)GetValue(ArtWidthProperty);
        set => SetValue(ArtWidthProperty, value);
    }

    private void Draw() => Content = Type switch
    {
        Core.DeviceType.Laptop => ComputerDrawing.Laptop(ArtWidth * 1.7),
        Core.DeviceType.Desktop => ComputerDrawing.Desktop(ArtWidth * 1.7),
        Core.DeviceType.Tablet => new PhoneArt { ArtWidth = ArtWidth * 1.4 },
        Core.DeviceType.Tv => ComputerDrawing.Tv(ArtWidth * 1.9),
        _ => new PhoneArt { ArtWidth = ArtWidth },
    };
}

/// Charge as a ring, green when fine, amber when low, with a bolt while charging.
public sealed class BatteryRing : UserControl
{
    public static readonly DependencyProperty ChargeProperty =
        DependencyProperty.Register(nameof(Charge), typeof(int), typeof(BatteryRing), new PropertyMetadata(-1, (d, _) => ((BatteryRing)d).Draw()));

    public static readonly DependencyProperty IsChargingProperty =
        DependencyProperty.Register(nameof(IsCharging), typeof(bool), typeof(BatteryRing), new PropertyMetadata(false, (d, _) => ((BatteryRing)d).Draw()));

    private const double Size = 54;
    private const double Thickness = 5;

    private readonly Microsoft.UI.Xaml.Shapes.Path arc = new() { StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock label = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly LucideIcon bolt = new() { Icon = Lucide.Zap, Size = 9, HorizontalAlignment = HorizontalAlignment.Center };

    public BatteryRing()
    {
        var track = new Ellipse { StrokeThickness = Thickness, Width = Size, Height = Size };
        track.SetValue(Shape.StrokeProperty, Application.Current.Resources["FlyyLinkTrackBrush"]);

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { label, bolt } };
        Content = new Grid { Width = Size, Height = Size, Children = { track, arc, middle } };

        Draw();
    }

    public int Charge
    {
        get => (int)GetValue(ChargeProperty);
        set => SetValue(ChargeProperty, value);
    }

    public bool IsCharging
    {
        get => (bool)GetValue(IsChargingProperty);
        set => SetValue(IsChargingProperty, value);
    }

    private void Draw()
    {
        var known = Charge >= 0;
        var fraction = Math.Clamp(known ? Charge : 0, 0, 100) / 100.0;
        var isLow = known && Charge <= 15 && !IsCharging;
        var color = Application.Current.Resources[isLow ? "FlyyLinkWarnBrush" : "FlyyLinkOkBrush"];

        label.Text = known ? $"{Charge}%" : "–";
        bolt.Visibility = IsCharging ? Visibility.Visible : Visibility.Collapsed;
        bolt.SetValue(ForegroundProperty, color);
        arc.SetValue(Shape.StrokeProperty, color);
        arc.Data = Arc(fraction);
        AutomationProperties.SetName(this, known ? $"Battery {Charge} percent{(IsCharging ? ", charging" : "")}" : "Battery unknown");
    }

    /// The charged part of the ring, clockwise from the top.
    private static Geometry? Arc(double fraction)
    {
        if (fraction <= 0) return null;

        var radius = (Size - Thickness) / 2;
        var center = Size / 2;
        var angle = Math.Min(fraction, 0.9999) * 2 * Math.PI;
        var start = new Point(center, center - radius);
        var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Windows.Foundation.Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = fraction > 0.5,
        });

        return new PathGeometry { Figures = { figure } };
    }
}

/// Mobile signal as four rising bars, with the network type beside them, as a phone's status bar shows it.
public sealed class SignalBars : UserControl
{
    public static readonly DependencyProperty StrengthProperty =
        DependencyProperty.Register(nameof(Strength), typeof(int), typeof(SignalBars), new PropertyMetadata(0, (d, _) => ((SignalBars)d).Draw()));

    public static readonly DependencyProperty GenerationProperty =
        DependencyProperty.Register(nameof(Generation), typeof(string), typeof(SignalBars), new PropertyMetadata("", (d, _) => ((SignalBars)d).Draw()));

    private const double BarHeight = 11;
    private static readonly SolidColorBrush On = new(Geometries.Argb(0xCC1B1B1F));
    private static readonly SolidColorBrush Off = new(Geometries.Argb(0x2E1B1B1F));

    private readonly Rectangle[] bars = new Rectangle[Core.CellSignal.MaxStrength];
    private readonly TextBlock label = new() { FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Geometries.Argb(0xBF1B1B1F)), VerticalAlignment = VerticalAlignment.Center };

    public SignalBars()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1.5, VerticalAlignment = VerticalAlignment.Bottom, Height = BarHeight };
        for (var i = 0; i < bars.Length; i++)
        {
            bars[i] = new Rectangle { Width = 3, Height = BarHeight * (i + 1) / bars.Length, RadiusX = 1, RadiusY = 1, VerticalAlignment = VerticalAlignment.Bottom };
            row.Children.Add(bars[i]);
        }

        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { row, label } };

        Draw();
    }

    public int Strength
    {
        get => (int)GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    public string Generation
    {
        get => (string)GetValue(GenerationProperty);
        set => SetValue(GenerationProperty, value);
    }

    public static string Label(int strength, string generation)
    {
        if (strength <= 0) return "No signal";

        var count = $"{strength} of {Core.CellSignal.MaxStrength} bars";
        return string.IsNullOrEmpty(generation) ? count : $"{generation}, {count}";
    }

    private void Draw()
    {
        for (var i = 0; i < bars.Length; i++) bars[i].Fill = i < Strength ? On : Off;

        label.Text = Generation ?? "";
        label.Visibility = string.IsNullOrEmpty(Generation) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, Label(Strength, Generation ?? ""));
    }
}
