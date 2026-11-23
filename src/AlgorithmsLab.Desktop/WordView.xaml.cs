using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlgorithmsLab.Application;
using AlgorithmsLab.Domain;
using Microsoft.Win32;

namespace AlgorithmsLab.Desktop;

public partial class WordView : UserControl
{
    private const int ShownWords = 18;
    private readonly WordSorting sorting = new(new QuickSort(), new RadixSort());
    private CancellationTokenSource? measurement;
    private Task? benchmarkTask;
    private bool closed;
    private string[] words = [];

    public WordView()
    {
        InitializeComponent();
        Playback.Delay = 40;
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
        if (Tokens is null)
            return;

        UpdatePreview();
        ResetTimings();
    }

    private void UpdatePreview()
    {
        words = WordSorting.Tokenize(InputText.Text);
        Draw(words, new(StepKind.Start, ""));
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
        if (Method is not null && Tokens is not null)
        {
            DescribeMethod();
            Draw(words, new(StepKind.Start, ""));
        }
    }

    private void DescribeMethod()
    {
        Stage.Text = "Как это работает";
        Explanation.Text = Method.SelectedIndex == (int)WordMethod.Radix
            ? "Смотрим на буквы справа налево. На каждом шаге раскладываем слова в корзины и собираем корзины по порядку."
            : "Берём слово за ориентир. Слова, которые должны идти раньше него, перемещаем влево, остальные — вправо.";
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
        try
        {
            await Playback.RunAsync((report, token) => sorting.Run(input, method, (step, values) => report(step, () =>
            {
                Stage.Text = step.Label ?? Stage.Text;
                Explanation.Text = step.Message;
                Draw(values, step);
            }), token), result =>
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

    private void Draw(string[] values, SortStep step)
    {
        var first = Math.Clamp(step.First - ShownWords / 2, 0, Math.Max(0, values.Length - ShownWords));
        var length = Math.Min(ShownWords, values.Length - first);
        var tiles = new WordTile[length];
        for (var offset = 0; offset < length; offset++)
        {
            var index = first + offset;
            var highlight = index == step.First || index == step.Second
                ? step.Kind == StepKind.Compare ? "Compare" : "Change"
                : index >= step.RangeStart && index <= step.RangeEnd ? "Range" : "";
            tiles[offset] = new(values[index], highlight);
        }

        Tokens.ItemsSource = tiles;
        WindowCaption.Text = length == 0 ? "0 слов" : $"{first + 1}–{first + length} / {values.Length:N0}";
    }

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

    private sealed record WordTile(string Word, string Highlight);
    private sealed record TimingRow(int WordCount, double? QuickMilliseconds, double? RadixMilliseconds);
}
