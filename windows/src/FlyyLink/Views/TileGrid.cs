using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FlyyLink.Views;

/// Lays out the visible tiles two to a row, so hiding one leaves no gap.
public sealed class TileGrid : Panel
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(TileGrid), new PropertyMetadata(10.0, (d, _) => ((TileGrid)d).InvalidateMeasure()));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private IEnumerable<UIElement> Visible => Children.Where(child => child.Visibility == Visibility.Visible);

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 0 : available.Width;
        var cell = new Size(Math.Max(0, (width - Spacing) / 2), double.PositiveInfinity);
        var rows = Rows(child => child.Measure(cell));

        return new Size(width, Height(rows));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var cellWidth = Math.Max(0, (final.Width - Spacing) / 2);
        var rows = Rows(_ => { });
        var y = 0.0;

        foreach (var row in rows)
        {
            var height = row.Max(child => child.DesiredSize.Height);
            for (var column = 0; column < row.Count; column++)
            {
                row[column].Arrange(new Rect(column * (cellWidth + Spacing), y, cellWidth, height));
            }

            y += height + Spacing;
        }

        foreach (var hidden in Children.Where(child => child.Visibility != Visibility.Visible)) hidden.Arrange(new Rect());

        return final;
    }

    private List<List<UIElement>> Rows(Action<UIElement> each)
    {
        var visible = Visible.ToList();
        visible.ForEach(each);

        return visible.Chunk(2).Select(pair => pair.ToList()).ToList();
    }

    private double Height(List<List<UIElement>> rows) =>
        rows.Count == 0 ? 0 : rows.Sum(row => row.Max(child => child.DesiredSize.Height)) + Spacing * (rows.Count - 1);
}
