using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Desktop;

public sealed class WordTapeChart : FrameworkElement
{
    private const int MaxVisibleWords = 160;
    private const double PadX = 8;
    private const double PadY = 8;
    private const double GapX = 6;
    private const double GapY = 18;
    private const double ChipHeight = 28;
    private const double ChipPadX = 9;
    private const double MaxChipWidth = 148;

    private static readonly Typeface Mono = new("Consolas");
    private static readonly Typeface MonoBold = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private readonly Stopwatch animClock = new();
    private string[] words = [];
    private SortStep step = new(StepKind.Start, "");
    private int lower;
    private int upper = -1;
    private int windowStart;
    private int windowLength;
    private bool animatingSwap;
    private double animDurationMs;
    private double animProgress = 1;
    private SlotRect[] cachedSlots = [];
    private double cachedWidth = -1;

    public WordTapeChart()
    {
        Unloaded += (_, _) => StopAnimation();
    }

    public string WindowSummary =>
        windowLength == 0
            ? "0 слов"
            : windowLength == words.Length
                ? $"Все слова: {words.Length:N0}"
                : $"Показаны {windowStart + 1}–{windowStart + windowLength} из {words.Length:N0}";

    internal void Draw(string[] items, SortStep frame, int animationMs = 0)
    {
        StopAnimation();
        words = (string[])items.Clone();
        step = frame;
        if (step.Kind is StepKind.Start or StepKind.Complete)
            (lower, upper) = (0, words.Length - 1);
        else if (step.RangeStart >= 0)
            (lower, upper) = (step.RangeStart, step.RangeEnd);
        else if (step.CharPosition >= 0)
            (lower, upper) = (0, words.Length - 1);

        if (words.Length <= MaxVisibleWords)
        {
            windowStart = 0;
            windowLength = words.Length;
        }
        else
        {
            var focus = step.First >= 0 ? step.First : 0;
            windowStart = Math.Clamp(focus - MaxVisibleWords / 2, 0, Math.Max(0, words.Length - MaxVisibleWords));
            windowLength = Math.Min(MaxVisibleWords, words.Length - windowStart);
        }

        cachedWidth = -1;

        if (frame.Kind == StepKind.Swap &&
            animationMs > 0 &&
            frame.First >= 0 && frame.Second >= 0 &&
            frame.First != frame.Second &&
            frame.First < words.Length && frame.Second < words.Length)
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

        InvalidateMeasure();
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

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) || availableSize.Width < 120 ? 640 : availableSize.Width;
        var height = ComputeLayout(width);
        return new Size(width, Math.Max(70, height));
    }

    private double ComputeLayout(double availableWidth)
    {
        if (Math.Abs(cachedWidth - availableWidth) < 0.5 && cachedSlots.Length == windowLength)
            return cachedSlots.Length == 0 ? 70 : cachedSlots[^1].Rect.Bottom + PadY;

        cachedWidth = availableWidth;
        cachedSlots = new SlotRect[windowLength];
        if (windowLength == 0)
            return 70;

        var culture = CultureInfo.CurrentCulture;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var textBrush = BrushFor("Text");
        var x = PadX;
        var y = PadY + 13;
        var maxRight = Math.Max(PadX + 120, availableWidth - PadX);

        for (var i = 0; i < windowLength; i++)
        {
            var index = windowStart + i;
            var word = words[index];
            var display = step.CharPosition >= 0 && step.CharPosition >= word.Length ? word + "∅" : word;
            var ft = new FormattedText(display, culture, FlowDirection.LeftToRight, MonoBold, 13, textBrush, dpi);
            var chipWidth = Math.Clamp(ft.Width + ChipPadX * 2 + (index == step.Pivot && step.Kind != StepKind.Complete ? 14 : 0), 34, MaxChipWidth);

            if (x > PadX && x + chipWidth > maxRight)
            {
                x = PadX;
                y += ChipHeight + GapY;
            }

            cachedSlots[i] = new SlotRect(index, new Rect(x, y, chipWidth, ChipHeight));
            x += chipWidth + GapX;
        }

        return y + ChipHeight + PadY;
    }

    protected override void OnRender(DrawingContext graphics)
    {
        base.OnRender(graphics);
        if (windowLength == 0 || ActualWidth < 60)
            return;

        ComputeLayout(ActualWidth);

        var culture = CultureInfo.CurrentCulture;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var subtleBrush = BrushFor("Subtle");
        var pivotBrush = BrushFor("Pivot");
        var hasPivot = step.Pivot >= 0 && step.Pivot < words.Length && step.Kind != StepKind.Complete;
        var hasSubrange = step.Kind is not StepKind.Start and not StepKind.Complete &&
                          step.CharPosition < 0 &&
                          lower >= 0 && upper >= lower && upper < words.Length &&
                          (lower > 0 || upper < words.Length - 1);

        var firstSlot = FindVisibleSlot(step.First);
        var secondSlot = FindVisibleSlot(step.Second);
        var hasSwapPair = step.Kind == StepKind.Swap &&
                          firstSlot >= 0 && secondSlot >= 0 &&
                          firstSlot != secondSlot;

        // Номера позиций над слотами (фиксированы на месте даже во время обмена).
        for (var i = 0; i < cachedSlots.Length; i++)
        {
            var slot = cachedSlots[i];
            var idx = slot.Index;
            var isPivotSlot = hasPivot && idx == step.Pivot;
            var idxBrush = isPivotSlot ? pivotBrush : subtleBrush;
            var idxText = Format((idx + 1).ToString(culture), 9.5, idxBrush, slot.Rect.Width, bold: isPivotSlot);
            graphics.DrawText(idxText, new(slot.Rect.X + (slot.Rect.Width - idxText.Width) / 2, slot.Rect.Y - 12));
        }

        // Неподвижные карточки слов.
        for (var i = 0; i < cachedSlots.Length; i++)
        {
            if (hasSwapPair && (i == firstSlot || i == secondSlot))
                continue;

            DrawWordChip(cachedSlots[i].Index, cachedSlots[i].Rect, isSwapping: false);
        }

        // Обменивающиеся слова с плавной анимацией перелёта.
        if (hasSwapPair)
        {
            var t = animatingSwap ? animProgress : 1;
            var rectFirst = cachedSlots[firstSlot].Rect;
            var rectSecond = cachedSlots[secondSlot].Rect;

            // Пунктирная дуга между обменивающимися позициями.
            var c1 = new Point(rectFirst.X + rectFirst.Width / 2, rectFirst.Y);
            var c2 = new Point(rectSecond.X + rectSecond.Width / 2, rectSecond.Y);
            var mid = new Point((c1.X + c2.X) / 2, Math.Min(c1.Y, c2.Y) - 16);
            var arcPen = new Pen(BrushFor("Change"), 1.5) { DashStyle = DashStyles.Dot };
            var arc = new StreamGeometry();
            using (var ctx = arc.Open())
            {
                ctx.BeginFigure(c1, false, false);
                ctx.QuadraticBezierTo(mid, c2, true, false);
            }
            arc.Freeze();
            graphics.DrawGeometry(null, arcPen, arc);

            // words уже содержит значения после обмена:
            // слово в step.First перелетает из rectSecond -> rectFirst,
            // слово в step.Second перелетает из rectFirst -> rectSecond.
            var lift = animatingSwap ? Math.Sin(t * Math.PI) * 14 : 0;
            var r1 = InterpolateRect(rectSecond, rectFirst, t, -lift);
            var r2 = InterpolateRect(rectFirst, rectSecond, t, lift * 0.55);

            DrawWordChip(step.First, r1, isSwapping: true);
            DrawWordChip(step.Second, r2, isSwapping: true);
        }

        int FindVisibleSlot(int index)
        {
            if (index < windowStart || index >= windowStart + windowLength)
                return -1;
            return index - windowStart;
        }

        static Rect InterpolateRect(Rect from, Rect to, double t, double offsetY) =>
            new(from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t + offsetY,
                from.Width + (to.Width - from.Width) * t,
                from.Height);

        void DrawWordChip(int index, Rect rect, bool isSwapping)
        {
            var word = words[index];
            var isPivot = hasPivot && index == step.Pivot;
            var isAction = index == step.First || index == step.Second;
            var isRadixWrite = step.Kind == StepKind.Write && step.BucketLabel is not null;
            var isRadixConsumed = isRadixWrite && index < step.First;
            var isOutsideRange = hasSubrange && (index < lower || index > upper);
            var isCountGroup = step.Kind == StepKind.Phase && step.RangeStart >= 0 &&
                               step.CharPosition < 0 && !hasPivot &&
                               index >= step.RangeStart && index <= step.RangeEnd;

            Brush bg;
            Brush borderBrush;
            double borderThickness = 1;
            double opacity = 1;

            if (isPivot)
            {
                bg = BrushFor("PivotPanel");
                borderBrush = isAction && step.Kind is StepKind.Compare or StepKind.Swap
                    ? BrushFor(step.Kind == StepKind.Compare ? "Compare" : "Change")
                    : pivotBrush;
                borderThickness = 2;
            }
            else if (isRadixWrite && index == step.First)
            {
                bg = BrushFor("ChangePanel");
                borderBrush = BrushFor("Change");
                borderThickness = 1.8;
            }
            else if (isAction && !isRadixWrite && step.Kind is StepKind.Compare or StepKind.Swap or StepKind.Write)
            {
                var isCompare = step.Kind == StepKind.Compare;
                bg = BrushFor(isCompare ? "ComparePanel" : "ChangePanel");
                borderBrush = BrushFor(isCompare ? "Compare" : "Change");
                borderThickness = isSwapping ? 2 : 1.6;
            }
            else if (step.Kind == StepKind.Complete || isCountGroup)
            {
                bg = BrushFor("AccentPanel");
                borderBrush = BrushFor("Accent");
                borderThickness = 1.3;
            }
            else if (hasSubrange && !isOutsideRange)
            {
                bg = BrushFor("AccentPanel");
                borderBrush = BrushFor("Bar");
            }
            else
            {
                bg = BrushFor("Card");
                borderBrush = BrushFor("Border");
            }

            if (isRadixConsumed || isOutsideRange)
                opacity = 0.38;

            if (opacity < 1)
                graphics.PushOpacity(opacity);

            graphics.DrawRoundedRectangle(bg, new Pen(borderBrush, borderThickness), rect, 6, 6);

            var contentX = rect.X + ChipPadX;
            var maxContentWidth = Math.Max(10, rect.Width - ChipPadX * 2);

            if (isPivot)
            {
                var star = Format("★", 11, pivotBrush, 14, bold: true);
                graphics.DrawText(star, new(contentX - 2, rect.Y + (rect.Height - star.Height) / 2));
                contentX += 13;
                maxContentWidth = Math.Max(10, maxContentWidth - 13);
            }

            if (step.CharPosition >= 0 && step.Kind != StepKind.Complete)
            {
                DrawRadixHighlightedWord(word, step.CharPosition, contentX, rect.Y, rect.Height, maxContentWidth);
            }
            else
            {
                var textBrush = isPivot ? pivotBrush
                    : isSwapping ? BrushFor("Change")
                    : isAction && step.Kind == StepKind.Compare ? BrushFor("Compare")
                    : BrushFor("Text");
                var ft = Format(word, 13, textBrush, maxContentWidth, bold: isPivot || isSwapping);
                graphics.DrawText(ft, new(contentX, rect.Y + (rect.Height - ft.Height) / 2));
            }

            if (opacity < 1)
                graphics.Pop();
        }

        void DrawRadixHighlightedWord(string word, int charPos, double startX, double topY, double height, double maxWidth)
        {
            var textBrush = BrushFor("Text");
            var accentBrush = BrushFor("Accent");
            var compareBrush = BrushFor("Compare");
            var highlightBg = BrushFor("ComparePanel");

            var x = startX;
            if (charPos < word.Length)
            {
                var before = word[..charPos];
                var active = word[charPos].ToString();
                var after = word[(charPos + 1)..];

                if (before.Length > 0)
                {
                    var ftBefore = Format(before, 13, textBrush, maxWidth);
                    graphics.DrawText(ftBefore, new(x, topY + (height - ftBefore.Height) / 2));
                    x += ftBefore.Width;
                }

                var ftActive = Format(active, 13, compareBrush, Math.Max(12, startX + maxWidth - x), bold: true);
                var pill = new Rect(x - 1.5, topY + 4, ftActive.Width + 3, height - 8);
                graphics.DrawRoundedRectangle(highlightBg, new Pen(compareBrush, 1), pill, 3, 3);
                graphics.DrawText(ftActive, new(x, topY + (height - ftActive.Height) / 2));
                x += ftActive.Width + 1;

                if (after.Length > 0 && x < startX + maxWidth - 4)
                {
                    var ftAfter = Format(after, 13, textBrush, Math.Max(4, startX + maxWidth - x));
                    graphics.DrawText(ftAfter, new(x, topY + (height - ftAfter.Height) / 2));
                }
            }
            else
            {
                var ftWord = Format(word, 13, textBrush, Math.Max(10, maxWidth - 12));
                graphics.DrawText(ftWord, new(x, topY + (height - ftWord.Height) / 2));
                x += ftWord.Width + 2;
                var ftEmpty = Format("∅", 11.5, accentBrush, 14, bold: true);
                graphics.DrawText(ftEmpty, new(x, topY + (height - ftEmpty.Height) / 2));
            }
        }

        FormattedText Format(string value, double size, Brush brush, double limit, bool bold = false) =>
            new(value, culture, FlowDirection.LeftToRight, bold ? MonoBold : Mono, size, brush, dpi)
            {
                MaxTextWidth = Math.Max(1, limit),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
    }

    private Brush BrushFor(string key) => (Brush)FindResource(key);

    private readonly record struct SlotRect(int Index, Rect Rect);
}
