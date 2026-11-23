using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Desktop;

public sealed class SortChart : FrameworkElement
{
    private static readonly Typeface Digits = new("Consolas");
    private int[] values = [];
    private SortStep step = new(StepKind.Start, "");
    private int lower;
    private int upper = -1;

    internal void Draw(int[] items, SortStep frame)
    {
        values = (int[])items.Clone();
        step = frame;
        if (step.Kind is StepKind.Start or StepKind.Complete)
            (lower, upper) = (0, values.Length - 1);
        else if (step.RangeStart >= 0)
            (lower, upper) = (step.RangeStart, step.RangeEnd);

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext graphics)
    {
        base.OnRender(graphics);
        if (values.Length == 0 || ActualWidth < 80 || ActualHeight < 80)
            return;

        var max = Math.Max(0d, values.Max());
        var min = Math.Min(0d, values.Min());
        if (max == min)
            max = 1;

        var targetInterval = Math.Max(1, (max - min) / 4);
        var unit = Math.Pow(10, Math.Floor(Math.Log10(targetInterval)));
        var interval = new[] { 1d, 2d, 2.5d, 5d, 10d }.First(factor => factor * unit >= targetInterval) * unit;
        interval = Math.Ceiling(interval);
        min = Math.Floor(min / interval) * interval;
        max = Math.Ceiling(max / interval) * interval;

        const double left = 48;
        const double top = 26;
        var bottom = ActualHeight - 36;
        var right = ActualWidth - 10;
        var zero = Coordinate(0);
        var spacing = (right - left) / values.Length;
        var width = Math.Min(40, spacing * 0.62);
        var culture = CultureInfo.CurrentCulture;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var line = new Pen(BrushFor("Border"), 1);
        var digits = BrushFor("Muted");

        for (var value = min; value <= max; value += interval)
        {
            var y = Coordinate(value);
            graphics.DrawLine(line, new(left, y), new(right, y));
            var label = Math.Abs(value) >= 100_000 ? value.ToString("0.#E+0", culture) : value.ToString("0.##", culture);
            var text = Format(label, 10, digits, 44);
            graphics.DrawText(text, new(left - text.Width - 5, y - text.Height / 2));
        }

        graphics.DrawLine(line, new(left, zero), new(right, zero));
        for (var pos = 0; pos < values.Length; pos++)
        {
            var x = left + (pos + 0.5) * spacing;
            var y = Coordinate(values[pos]);
            var brush = pos == step.First || pos == step.Second
                ? BrushFor(step.Kind == StepKind.Compare ? "Compare" : step.Kind == StepKind.Phase ? "Accent" : "Change")
                : BrushFor(pos < lower || pos > upper ? "DimBar" : step.Kind == StepKind.Complete ? "Accent" : "Bar");

            graphics.DrawRoundedRectangle(brush, null, new(x - width / 2, Math.Min(y, zero), width,
                Math.Max(2, Math.Abs(y - zero))), 2, 2);
            if (spacing >= 26 || pos == step.First || pos == step.Second)
            {
                var formatted = Format(values[pos].ToString(culture), 12, BrushFor("Text"), spacing - 2);
                graphics.DrawText(formatted, new(x - formatted.Width / 2, values[pos] >= 0 ? y - 20 : y + 3));
            }

            var position = Format((pos + 1).ToString(culture), 10, digits, spacing - 1);
            graphics.DrawText(position, new(x - position.Width / 2, bottom + 19));
        }

        double Coordinate(double value) => bottom - (bottom - top) * (value - min) / (max - min);

        FormattedText Format(string value, double size, Brush brush, double limit) =>
            new(value, culture, FlowDirection.LeftToRight, Digits, size, brush, pixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, limit),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
    }

    private Brush BrushFor(string key) => (Brush)FindResource(key);
}
