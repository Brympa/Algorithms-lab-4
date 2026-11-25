using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlgorithmsLab.Application;
using AlgorithmsLab.Domain;
using Microsoft.Win32;

namespace AlgorithmsLab.Desktop;

public partial class WordView : UserControl
{
    private const int MaxWordsPerBucket = 80;
    private readonly WordSorting sorting = new(new QuickSort(), new RadixSort());
    private CancellationTokenSource? measurement;
    private Task? benchmarkTask;
    private bool closed;
    private string[] words = [];

    public WordView()
    {
        InitializeComponent();
        Playback.Delay = 120;
        Method.SelectedIndex = 0;
        UpdatePreview();
        ResetTimings();
    }

    private void OpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Текст (*.txt)|*.txt|Все файлы|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            ReadFile(dialog.FileName);
    }

    private void LoadCorpus(object sender, RoutedEventArgs e) =>
        ReadFile(Path.Combine(AppContext.BaseDirectory, "Examples", "corpus.txt"));

    private void ReadFile(string path)
    {
        try
        {
            InputText.Text = File.ReadAllText(path);
            Playback.ShowStatus($"Текст загружен · слов {words.Length:N0}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Playback.ShowStatus(error.Message, error: true);
        }
    }

    private void TextEdited(object sender, TextChangedEventArgs e)
    {
        if (WordTape is null)
            return;

        UpdatePreview();
        ResetTimings();
    }

    private void UpdatePreview()
    {
        words = WordSorting.Tokenize(InputText.Text);
        DrawWordTape(words, new(StepKind.Start, ""), animationMs: 0);
        ResetRadixBuckets();
        if (Frequencies is not null)
        {
            Frequencies.Visibility = Visibility.Collapsed;
            NoFrequencies.Visibility = Visibility.Visible;
            Frequencies.ItemsSource = null;
        }

        DescribeMethod();
    }

    private void ResetTimings() =>
        Timings.ItemsSource = WordSorting.BenchmarkSizes.Select(size => new TimingRow(size, null, null)).ToArray();

    private void SelectMethod(object sender, SelectionChangedEventArgs e)
    {
        if (Method is not null && WordTape is not null)
        {
            DescribeMethod();
            DrawWordTape(words, new(StepKind.Start, ""), animationMs: 0);
            ResetRadixBuckets();
        }
    }

    private void DescribeMethod()
    {
        var isRadix = Method.SelectedIndex == (int)WordMethod.Radix;
        Stage.Text = "Как это работает";
        Explanation.Text = isRadix
            ? "Просматриваем символы справа налево. На каждом разряде раскладываем слова по корзинам-лентам и собираем их по порядку."
            : "Выбираем опорное слово (Pivot). Слова, идущие раньше него, переносим влево, остальные — вправо, анимируя каждый обмен.";
        PivotLegend.Visibility = isRadix ? Visibility.Collapsed : Visibility.Visible;
        RadixSection.Visibility = isRadix ? Visibility.Visible : Visibility.Collapsed;
        RadixFlowCaption.Text = "↓  Корзины-ленты поразрядной сортировки  ↓";
    }

    private void ResetRadixBuckets()
    {
        if (BucketList is null)
            return;

        BucketList.ItemsSource = null;
        EmptyBuckets.Visibility = Visibility.Visible;
        BucketSummary.Text = "";
        PivotBadge.Visibility = Visibility.Collapsed;
    }

    private async void Start(object sender, RoutedEventArgs e)
    {
        if (!HasWords())
            return;

        var input = words;
        var method = (WordMethod)Method.SelectedIndex;
        Inputs.IsEnabled = false;
        Frequencies.Visibility = Visibility.Collapsed;
        NoFrequencies.Visibility = Visibility.Visible;
        ResetRadixBuckets();
        RadixSection.Visibility = method == WordMethod.Radix ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            await Playback.RunAsync((report, token) =>
            {
                var buckets = new SortedDictionary<string, List<string>>(BucketKeyComparer.Instance);
                string? activeBucket = null;
                var radixFlow = "↓  Корзины-ленты поразрядной сортировки  ↓";

                return sorting.Run(input, method, (step, values) =>
                {
                    if (method == WordMethod.Radix)
                    {
                        if (step.Kind == StepKind.Phase && step.CharPosition >= 0)
                        {
                            var isCollected = step.Label?.StartsWith("Корзины собраны", StringComparison.Ordinal) == true;
                            if (!isCollected)
                            {
                                buckets.Clear();
                                activeBucket = null;
                                radixFlow = $"↓  Раскладка по корзинам · символ №{step.CharPosition + 1}  ↓";
                            }
                            else
                            {
                                activeBucket = null;
                                radixFlow = $"↑  Корзины собраны в ленту слов · после символа №{step.CharPosition + 1}  ↑";
                            }
                        }
                        else if (step.Kind == StepKind.Write && step.BucketLabel is { } key && step.First >= 0 && step.First < values.Length)
                        {
                            if (!buckets.TryGetValue(key, out var list))
                            {
                                list = [];
                                buckets[key] = list;
                            }

                            list.Add(values[step.First]);
                            activeBucket = key;
                            radixFlow = $"↓  Слово «{values[step.First]}» → корзина «{key}» (символ №{step.CharPosition + 1})  ↓";
                        }
                        else if (step.Kind == StepKind.Complete)
                        {
                            activeBucket = null;
                            radixFlow = "✓  Сортировка и подсчёт частот завершены";
                        }
                    }

                    report(step, () =>
                    {
                        Stage.Text = step.Label ?? Stage.Text;
                        Explanation.Text = step.Message;
                        DrawWordTape(values, step, Playback.CurrentAnimationMs);

                        if (method == WordMethod.Radix)
                        {
                            var bucketSnapshot = BuildBucketViews(buckets, activeBucket);
                            var totalInBuckets = buckets.Values.Sum(list => list.Count);
                            RadixFlowCaption.Text = radixFlow;
                            BucketSummary.Text = buckets.Count == 0 ? "" : $"Корзин: {buckets.Count} · слов: {totalInBuckets:N0}";
                            BucketList.ItemsSource = bucketSnapshot;
                            EmptyBuckets.Visibility = bucketSnapshot.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
                        }
                    });
                }, token);
            }, result =>
            {
                Frequencies.ItemsSource = result;
                Frequencies.Visibility = Visibility.Visible;
                NoFrequencies.Visibility = Visibility.Collapsed;
            });
        }
        finally
        {
            Inputs.IsEnabled = true;
        }
    }

    private static BucketTapeView[] BuildBucketViews(SortedDictionary<string, List<string>> buckets, string? activeBucket)
    {
        var result = new BucketTapeView[buckets.Count];
        var index = 0;
        foreach (var (key, list) in buckets)
        {
            var isActive = key == activeBucket;
            var take = Math.Min(list.Count, MaxWordsPerBucket);
            var start = list.Count - take;
            var chips = new List<BucketWordChip>(take + (start > 0 ? 1 : 0));
            if (start > 0)
                chips.Add(new($"+{start}…", false));

            for (var i = start; i < list.Count; i++)
                chips.Add(new(list[i], isActive && i == list.Count - 1));

            var label = key == "∅" ? "∅" : $"«{key}»";
            result[index++] = new(label, $"{list.Count} сл.", chips.ToArray(), isActive);
        }

        return result;
    }

    private async void Benchmark(object sender, RoutedEventArgs e)
    {
        if (!HasWords())
            return;

        Inputs.IsEnabled = false;
        Playback.ShowStatus("Замеряем оба алгоритма");
        using var source = new CancellationTokenSource();
        measurement = source;
        var corpus = words;
        try
        {
            var task = Task.Run(() => sorting.Benchmark(corpus, source.Token), source.Token);
            benchmarkTask = task;
            var results = await task;
            if (!closed)
            {
                Timings.ItemsSource = results;
                Playback.ShowStatus("Замеры завершены");
                Timings.BringIntoView();
            }
        }
        catch (OperationCanceledException) when (closed)
        {
        }
        catch (Exception error)
        {
            if (!closed)
                Playback.ShowStatus(error.Message, error: true);
        }
        finally
        {
            measurement = null;
            benchmarkTask = null;
            Inputs.IsEnabled = true;
        }
    }

    private bool HasWords()
    {
        if (words.Length > 0)
            return true;

        Playback.ShowStatus("Введите текст хотя бы с одним словом.", error: true);
        return false;
    }

    private void DrawWordTape(string[] values, SortStep step, int animationMs)
    {
        WordTape.Draw(values, step, animationMs);
        WindowCaption.Text = WordTape.WindowSummary;

        if (step.Pivot >= 0 && step.Pivot < values.Length && step.Kind != StepKind.Complete)
        {
            PivotBadge.Visibility = Visibility.Visible;
            PivotBadgeText.Text = $"★ Опора (Pivot): «{values[step.Pivot]}»";
        }
        else
        {
            PivotBadge.Visibility = Visibility.Collapsed;
        }
    }

    public void RefreshTheme() => WordTape.InvalidateVisual();

    public async Task CloseAsync()
    {
        closed = true;
        measurement?.Cancel();
        try
        {
            if (benchmarkTask is { } pending)
                await pending;
        }
        catch (Exception)
        {
            // Обработчик Benchmark уже обработал результат замера.
        }
        await Playback.CloseAsync();
    }

    private sealed class BucketKeyComparer : IComparer<string>
    {
        public static BucketKeyComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == "∅") return -1;
            if (y == "∅") return 1;
            return StringComparer.Ordinal.Compare(x, y);
        }
    }

    private sealed record BucketWordChip(string Word, bool IsNew);
    private sealed record BucketTapeView(string Label, string CountText, BucketWordChip[] Words, bool Active);
    private sealed record TimingRow(int WordCount, double? QuickMilliseconds, double? RadixMilliseconds);
}
