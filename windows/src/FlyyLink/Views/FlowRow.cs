using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FlyyLink.Views;

/// Lays its children out left to right at their natural size, starting a new line when the next one would not fit.
public sealed class FlowRow : Panel
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(FlowRow), new PropertyMetadata(6.0, (d, _) => ((FlowRow)d).InvalidateMeasure()));

    public static readonly DependencyProperty LineSpacingProperty =
        DependencyProperty.Register(nameof(LineSpacing), typeof(double), typeof(FlowRow), new PropertyMetadata(6.0, (d, _) => ((FlowRow)d).InvalidateMeasure()));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double LineSpacing
    {
        get => (double)GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var child in Children) child.Measure(unbounded);

        var lines = Lines(available.Width);
        var width = lines.Count == 0 ? 0 : lines.Max(line => line.Width);

        return new Size(width, LinesHeight(lines));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var y = 0.0;

        foreach (var line in Lines(final.Width))
        {
            var x = 0.0;
            foreach (var child in line.Children)
            {
                var size = child.DesiredSize;
                child.Arrange(new Rect(x, y + (line.Height - size.Height) / 2, size.Width, size.Height));
                x += size.Width + Spacing;
            }

            y += line.Height + LineSpacing;
        }

        foreach (var hidden in Children.Where(child => !Shows(child))) hidden.Arrange(new Rect());

        return final;
    }

    private sealed class Line
    {
        public List<UIElement> Children { get; } = new();
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// Collapsed children, and ones whose own children are all collapsed, take no room.
    private static bool Shows(UIElement child) =>
        child.Visibility == Visibility.Visible && child.DesiredSize.Width > 0;

    private List<Line> Lines(double width)
    {
        var lines = new List<Line>();
        var line = new Line();

        foreach (var child in Children.Where(Shows))
        {
            var size = child.DesiredSize;
            var gap = line.Children.Count == 0 ? 0 : Spacing;

            if (line.Children.Count > 0 && line.Width + gap + size.Width > width)
            {
                lines.Add(line);
                line = new Line();
                gap = 0;
            }

            line.Children.Add(child);
            line.Width += gap + size.Width;
            line.Height = Math.Max(line.Height, size.Height);
        }

        if (line.Children.Count > 0) lines.Add(line);

        return lines;
    }

    private double LinesHeight(List<Line> lines) =>
        lines.Count == 0 ? 0 : lines.Sum(line => line.Height) + LineSpacing * (lines.Count - 1);
}
