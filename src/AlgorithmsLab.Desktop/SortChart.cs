using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Desktop;

public sealed class SortChart : FrameworkElement
{
    private static readonly Typeface Digits = new("Consolas");
    private static readonly Typeface BoldDigits = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private readonly Stopwatch animClock = new();
    private int[] values = [];
    private SortStep step = new(StepKind.Start, "");
    private int lower;
    private int upper = -1;
    private bool animatingSwap;
    private double animDurationMs;
    private double animProgress = 1;

    public SortChart()
    {
        Unloaded += (_, _) => StopAnimation();
    }

    internal void Draw(int[] items, SortStep frame, int animationMs = 0)
    {
        StopAnimation();
        values = (int[])items.Clone();
        step = frame;
        if (step.Kind is StepKind.Start or StepKind.Complete)
            (lower, upper) = (0, values.Length - 1);
        else if (step.RangeStart >= 0)
            (lower, upper) = (step.RangeStart, step.RangeEnd);

        if (frame.Kind == StepKind.Swap &&
            animationMs > 0 &&
            frame.First >= 0 && frame.Second >= 0 &&
            frame.First != frame.Second &&
            frame.First < values.Length && frame.Second < values.Length)
        {
            animatingSwap = true;
            animDurationMs = animationMs;
            animProgress = 0;
            animClock.Restart();
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            animProgress = 1;
        }

        InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!animatingSwap)
            return;

        var raw = Math.Clamp(animClock.Elapsed.TotalMilliseconds / animDurationMs, 0, 1);
        animProgress = EaseInOutCubic(raw);
        InvalidateVisual();

        if (raw >= 1)
            StopAnimation();
    }

    private void StopAnimation()
    {
        if (!animatingSwap)
            return;

        animatingSwap = false;
        animProgress = 1;
        animClock.Stop();
        CompositionTarget.Rendering -= OnRendering;
    }

    private static double EaseInOutCubic(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

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
        const double top = 34;
        var bottom = ActualHeight - 36;
        var right = ActualWidth - 10;
        var zero = Coordinate(0);
        var spacing = (right - left) / values.Length;
        var width = Math.Min(40, spacing * 0.62);
        var culture = CultureInfo.CurrentCulture;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var line = new Pen(BrushFor("Border"), 1);
        var digits = BrushFor("Muted");
        var pivotBrush = BrushFor("Pivot");

        // Сетка значений по оси Y.
        for (var value = min; value <= max; value += interval)
        {
            var y = Coordinate(value);
            graphics.DrawLine(line, new(left, y), new(right, y));
            var label = Math.Abs(value) >= 100_000 ? value.ToString("0.#E+0", culture) : value.ToString("0.##", culture);
            var text = Format(label, 10, digits, 44);
            graphics.DrawText(text, new(left - text.Width - 5, y - text.Height / 2));
        }

        graphics.DrawLine(line, new(left, zero), new(right, zero));

        // Подсветка активного диапазона снизу.
        if (step.Kind is not StepKind.Start and not StepKind.Complete &&
            lower >= 0 && upper >= lower && upper < values.Length &&
            (lower > 0 || upper < values.Length - 1))
        {
            var rangeLeft = SlotX(lower) - spacing * 0.45;
            var rangeRight = SlotX(upper) + spacing * 0.45;
            var rangePen = new Pen(BrushFor("Bar"), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            graphics.DrawLine(rangePen, new(rangeLeft, bottom + 12), new(rangeRight, bottom + 12));
        }

        // Номера позиций всегда остаются на своих местах.
        for (var pos = 0; pos < values.Length; pos++)
        {
            var x = SlotX(pos);
            var isSlotHighlighted = pos == step.First || pos == step.Second || pos == step.Pivot;
            var posBrush = pos == step.Pivot && step.Kind != StepKind.Complete
                ? pivotBrush
                : isSlotHighlighted ? BrushFor("Text") : digits;
            var position = Format((pos + 1).ToString(culture), 10, posBrush, spacing - 1);
            graphics.DrawText(position, new(x - position.Width / 2, bottom + 19));
        }

        var hasSwapPair = step.Kind == StepKind.Swap &&
                          step.First >= 0 && step.Second >= 0 &&
                          step.First != step.Second &&
                          step.First < values.Length && step.Second < values.Length;

        var hasPivot = step.Pivot >= 0 && step.Pivot < values.Length && step.Kind != StepKind.Complete;

        // Сначала рисуем неподвижные столбцы.
        for (var pos = 0; pos < values.Length; pos++)
        {
            if (hasSwapPair && (pos == step.First || pos == step.Second))
                continue;

            DrawBar(pos, SlotX(pos), liftY: 0, isSwapping: false);
        }

        // Пунктирная линия уровня опорного элемента (Pivot) по активному диапазону.
        if (hasPivot)
        {
            var pivotY = Coordinate(values[step.Pivot]);
            var rangeStart = lower >= 0 && lower < values.Length ? SlotX(lower) - spacing * 0.45 : left;
            var rangeEnd = upper >= 0 && upper < values.Length ? SlotX(upper) + spacing * 0.45 : right;
            var pivotPen = new Pen(pivotBrush, 1.5) { DashStyle = DashStyles.Dash };
            graphics.DrawLine(pivotPen, new(rangeStart, pivotY), new(rangeEnd, pivotY));
        }

        // Поверх остальных рисуем два обменивающихся столбца с плавной траекторией и дугой обмена.
        if (hasSwapPair)
        {
            var t = animatingSwap ? animProgress : 1;
            var xFirstSlot = SlotX(step.First);
            var xSecondSlot = SlotX(step.Second);

            // Дуга, показывающая направление обмена между двумя позициями.
            var arcMidX = (xFirstSlot + xSecondSlot) / 2;
            var arcTopY = Math.Max(8, top - 22);
            var swapArcPen = new Pen(BrushFor("Change"), 1.6) { DashStyle = DashStyles.Dot };
            var arcGeometry = new StreamGeometry();
            using (var ctx = arcGeometry.Open())
            {
                ctx.BeginFigure(new(xFirstSlot, top - 4), isFilled: false, isClosed: false);
                ctx.QuadraticBezierTo(new(arcMidX, arcTopY - 8), new(xSecondSlot, top - 4), isStroked: true, isSmoothJoin: false);
            }
            arcGeometry.Freeze();
            graphics.DrawGeometry(null, swapArcPen, arcGeometry);

            // values уже содержит переставленные значения:
            // элемент в step.First двигается из step.Second -> step.First,
            // элемент в step.Second двигается из step.First -> step.Second.
            var lift = animatingSwap ? Math.Sin(t * Math.PI) * 10 : 0;
            var x1 = Lerp(xSecondSlot, xFirstSlot, t);
            var x2 = Lerp(xFirstSlot, xSecondSlot, t);

            DrawBar(step.First, x1, liftY: -lift, isSwapping: true);
            DrawBar(step.Second, x2, liftY: lift * 0.35, isSwapping: true);
        }

        double SlotX(int index) => left + (index + 0.5) * spacing;
        double Coordinate(double value) => bottom - (bottom - top) * (value - min) / (max - min);
        static double Lerp(double a, double b, double t) => a + (b - a) * t;

        void DrawBar(int pos, double x, double liftY, bool isSwapping)
        {
            var val = values[pos];
            var y = Coordinate(val);
            var isPivot = hasPivot && pos == step.Pivot;
            var isAction = pos == step.First || pos == step.Second;

            Brush fill;
            Pen? stroke = null;

            if (isPivot)
            {
                fill = pivotBrush;
                if (isAction && step.Kind is StepKind.Compare or StepKind.Swap)
                    stroke = new Pen(BrushFor(step.Kind == StepKind.Compare ? "Compare" : "Change"), 2.2);
                else
                    stroke = new Pen(BrushFor("Text"), 1.2);
            }
            else if (isAction)
            {
                fill = BrushFor(step.Kind == StepKind.Compare ? "Compare" : step.Kind == StepKind.Phase ? "Accent" : "Change");
                if (isSwapping)
                    stroke = new Pen(BrushFor("Text"), 1.3);
            }
            else
            {
                fill = BrushFor(pos < lower || pos > upper ? "DimBar" : step.Kind == StepKind.Complete ? "Accent" : "Bar");
            }

            var barTop = Math.Min(y, zero) + liftY;
            var barHeight = Math.Max(2, Math.Abs(y - zero));
            graphics.DrawRoundedRectangle(fill, stroke, new(x - width / 2, barTop, width, barHeight), 3, 3);

            if (spacing >= 26 || isAction || isPivot)
            {
                var labelBrush = isPivot ? pivotBrush : BrushFor("Text");
                var formatted = Format(val.ToString(culture), 12, labelBrush, Math.Max(28, spacing - 2), bold: isPivot || isSwapping);
                var textY = val >= 0 ? barTop - 18 : barTop + barHeight + 3;
                graphics.DrawText(formatted, new(x - formatted.Width / 2, textY));

                if (isPivot)
                {
                    var badge = Format("★", 11, pivotBrush, 18, bold: true);
                    var badgeY = val >= 0 ? Math.Max(2, textY - 13) : textY + 14;
                    graphics.DrawText(badge, new(x - badge.Width / 2, badgeY));
                }
            }
            else if (isPivot)
            {
                var badge = Format("★", 10, pivotBrush, 16, bold: true);
                graphics.DrawText(badge, new(x - badge.Width / 2, Math.Max(2, barTop - 15)));
            }
        }

        FormattedText Format(string value, double size, Brush brush, double limit, bool bold = false) =>
            new(value, culture, FlowDirection.LeftToRight, bold ? BoldDigits : Digits, size, brush, pixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, limit),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
    }

    private Brush BrushFor(string key) => (Brush)FindResource(key);
}
