using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlgorithmsLab.Application;
using AlgorithmsLab.Domain;
using AlgorithmsLab.Infrastructure;
using Microsoft.Win32;

namespace AlgorithmsLab.Desktop;

public partial class ExternalView : UserControl
{
    private const int MaxVisibleItemsPerTape = 120;
    private readonly ExternalSorting sorting = new(new CsvTableStorage());
    private string? source;
    private string[] header = [];

    public ExternalView()
    {
        InitializeComponent();
        Playback.Delay = 120;
        FilterColumn.ItemsSource = new[] { "Без фильтра" };
        FilterColumn.SelectedIndex = 0;
        Separator.SelectedIndex = 0;
        Method.SelectedIndex = 0;
    }

    private TableFormat Format => new(Separator.SelectedIndex switch { 1 => ',', 2 => '\t', _ => ';' });

    private void OpenFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Таблицы (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Все файлы|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        source = dialog.FileName;
        if (Path.GetExtension(source).Equals(".tsv", StringComparison.OrdinalIgnoreCase))
            Separator.SelectedIndex = 2;
        ReadHeader();
    }

    private void OpenExample(object sender, RoutedEventArgs e)
    {
        source = Path.Combine(AppContext.BaseDirectory, "Examples", "countries.csv");
        Separator.SelectedIndex = 0;
        ReadHeader();
        if (header.Length >= 5)
        {
            Key.SelectedIndex = 4;
            KeyType.SelectedIndex = 1;
        }
    }

    private void ChangeSeparator(object sender, SelectionChangedEventArgs e)
    {
        if (source is not null)
            ReadHeader();
    }

    private void SelectFilter(object sender, SelectionChangedEventArgs e) =>
        FilterValue.IsEnabled = FilterColumn.SelectedIndex > 0;

    private void ReadHeader()
    {
        SourcePath.Text = source;
        SourcePath.ToolTip = source;
        header = [];
        try
        {
            header = sorting.ReadHeader(source!, Format);
            Playback.ShowStatus($"Открыта таблица · столбцов {header.Length}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            Playback.ShowStatus(error.Message, error: true);
        }

        Key.ItemsSource = header;
        Key.SelectedIndex = header.Length > 0 ? 0 : -1;
        FilterColumn.ItemsSource = new[] { "Без фильтра" }.Concat(header).ToArray();
        FilterColumn.SelectedIndex = 0;
        StartButton.IsEnabled = header.Length > 0;
    }

    private void SelectMethod(object sender, SelectionChangedEventArgs e)
    {
        MethodHint.Text = (MergeMethod)Method.SelectedIndex switch
        {
            MergeMethod.Natural => "Сначала находим уже упорядоченные серии и раскладываем их по 2 лентам, затем сливаем попарно.",
            MergeMethod.Multiway => "Распределяем естественные серии по 4 лентам и на каждом проходе сливаем до 4 серий одновременно.",
            _ => "Каждая строка сначала образует серию длины 1 на 2 лентах. Затем серии сливаются попарно, удваивая длину."
        };

        var count = Method.SelectedIndex == (int)MergeMethod.Multiway ? 4 : 2;
        InputSection.Visibility = Visibility.Collapsed;
        OutputSectionTitle.Text = "Временные ленты";
        OutputTapeList.ItemsSource = Enumerable.Range(1, count)
            .Select(number => new TapeView($"Лента {number}", "", "0 эл.", [], false, "Ожидает запуска")).ToArray();
        Pass.Text = "";
    }

    private async void Start(object sender, RoutedEventArgs e)
    {
        if (source is null || Key.SelectedIndex < 0)
            return;

        var save = new SaveFileDialog
        {
            Title = "Сохранить отсортированную таблицу",
            FileName = $"{Path.GetFileNameWithoutExtension(source)}-sorted",
            DefaultExt = Format.Separator == '\t' ? ".tsv" : ".csv",
            Filter = Format.Separator == '\t' ? "Таблица TSV|*.tsv" : "Таблица CSV|*.csv",
            AddExtension = true
        };
        if (save.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var filter = FilterColumn.SelectedIndex > 0 ? new TableFilter(FilterColumn.SelectedIndex - 1, FilterValue.Text) : null;
        var options = new ExternalSortOptions(source, save.FileName, Format, Key.SelectedIndex,
            (ColumnType)KeyType.SelectedIndex, (MergeMethod)Method.SelectedIndex, filter);
        Inputs.IsEnabled = false;
        Result.Text = "";
        EmptyPreview.Visibility = Visibility.Visible;
        Preview.ItemsSource = null;
        try
        {
            await Playback.RunAsync((report, token) =>
            {
                var tapeCount = options.Method == MergeMethod.Multiway ? 4 : 2;
                var sourceRun = new List<TapeEntry>();
                var inputTapes = CreateTapeBuffers(tapeCount);
                var outputTapes = CreateTapeBuffers(tapeCount);
                var consumedRows = new HashSet<long>();
                var tail = new Queue<string>();
                var previousPass = 0;
                var stage = "Разбиение на серии";
                long lastWrittenRow = -1;
                long lastSourceRow = -1;

                return sorting.Run(options, frame =>
                {
                    stage = frame.Step.Label ?? stage;
                    if (frame.Pass != previousPass)
                    {
                        inputTapes = outputTapes;
                        outputTapes = CreateTapeBuffers(tapeCount);
                        consumedRows.Clear();
                        tail.Clear();
                        lastWrittenRow = -1;
                        lastSourceRow = -1;
                        previousPass = frame.Pass;
                    }

                    if (frame.Pass == 0)
                    {
                        if (frame.Row is { } row)
                        {
                            var entry = CreateEntry(row, options.KeyColumn);
                            if (sourceRun.Count == 0 || sourceRun[^1].RowNumber != row.Number)
                                sourceRun.Add(entry);

                            if (frame.Step.Kind == StepKind.Write && frame.Tape >= 0)
                            {
                                AppendToTape(outputTapes[frame.Tape], entry, frame.Run, frame.StartsNewRun);
                                consumedRows.Add(row.Number);
                                lastWrittenRow = row.Number;
                            }
                        }
                    }
                    else
                    {
                        if (frame.Step.Kind == StepKind.Write && frame.Row is { } row && frame.Tape >= 0)
                        {
                            var entry = CreateEntry(row, options.KeyColumn);
                            AppendToTape(outputTapes[frame.Tape], entry, frame.Run, frame.StartsNewRun);
                            consumedRows.Add(row.Number);
                            lastWrittenRow = row.Number;
                            lastSourceRow = row.Number;

                            if (frame.WritesMergedRow)
                            {
                                if (tail.Count == 5)
                                    tail.Dequeue();
                                tail.Enqueue($"Строка {row.Number}  ·  {string.Join("  |  ", row.Fields)}");
                            }
                        }
                        else if (frame.Step.Kind != StepKind.Write)
                        {
                            lastSourceRow = -1;
                        }
                    }

                    var stageText = stage;
                    var isComplete = frame.Step.Kind == StepKind.Complete;
                    var passNumber = frame.Pass;
                    var messageText = frame.Step.Message;

                    report(frame.Step, () =>
                    {
                        var previewSnapshot = tail.Reverse().ToArray();
                        TapeView[] inputViews;
                        TapeView[] outputViews;
                        string inputTitle;
                        string outputTitle;
                        string flowText;

                        if (passNumber == 0)
                        {
                            inputTitle = "Входной файл (исходный поток)";
                            outputTitle = isComplete ? "Итоговые ленты (данные уже упорядочены)" : "Выходные ленты (запись серий)";
                            flowText = isComplete ? "✓  Сортировка завершена" : "↓  Распределение строк по лентам  ↓";

                            var incomingNumber = frame.Step.Kind is StepKind.Compare or StepKind.Phase && frame.Row is not null
                                ? frame.Row.Number
                                : -1L;
                            var sourceRuns = sourceRun.Count == 0
                                ? Array.Empty<RunView>()
                                : [BuildRunView("Вход", sourceRun, entry =>
                                {
                                    if (entry.RowNumber == incomingNumber)
                                        return frame.Step.Kind == StepKind.Compare ? "Compare" : "Head";
                                    return consumedRows.Contains(entry.RowNumber) ? "Consumed" : "Normal";
                                })];
                            inputViews =
                            [
                                new TapeView("Исходный файл",
                                    isComplete ? "Готово" : frame.Step.Kind == StepKind.Compare ? "Сравнение" : "Чтение",
                                    $"{sourceRun.Count} эл.",
                                    sourceRuns,
                                    !isComplete,
                                    "Чтение файла...")
                            ];

                            outputViews = new TapeView[tapeCount];
                            for (var i = 0; i < tapeCount; i++)
                            {
                                var isTarget = frame.Tape == i;
                                var compareTail = frame.Step.Kind == StepKind.Compare && isTarget
                                    ? LastRowNumber(outputTapes[i])
                                    : -1L;
                                var badge = isComplete && i == 0 ? "Отсортировано"
                                    : frame.Step.Kind == StepKind.Compare && isTarget ? "Сравнение конца"
                                    : isTarget && !isComplete ? $"Запись · С{frame.Run}" : "";
                                var tapeIndex = i;
                                outputViews[i] = BuildTapeView($"Лента {i + 1}", badge, outputTapes[i],
                                    (isTarget && !isComplete) || (isComplete && i == 0),
                                    entry =>
                                    {
                                        if (entry.RowNumber == compareTail)
                                            return "Compare";
                                        if (frame.Step.Kind == StepKind.Write && isTarget && entry.RowNumber == lastWrittenRow)
                                            return "Added";
                                        if (isComplete && tapeIndex == 0)
                                            return "Head";
                                        return "Normal";
                                    });
                            }
                        }
                        else
                        {
                            inputTitle = $"Входные ленты (чтение · после {(passNumber == 1 ? "разбиения" : $"прохода {passNumber - 1}")})";
                            outputTitle = isComplete ? "Итоговые ленты (результат на Ленте 1)" : $"Выходные ленты (запись · проход {passNumber})";
                            flowText = isComplete
                                ? "✓  Слияние завершено · все строки упорядочены"
                                : $"↓  Проход {passNumber} · слияние открытых серий  ↓";

                            inputViews = new TapeView[tapeCount];
                            for (var i = 0; i < tapeCount; i++)
                            {
                                var headRow = frame.HeadRows is not null && i < frame.HeadRows.Length ? frame.HeadRows[i] : -1L;
                                var isComparing = frame.Step.Kind == StepKind.Compare && (frame.CompareTapeA == i || frame.CompareTapeB == i);
                                var isSource = frame.Step.Kind == StepKind.Write && frame.SourceTape == i;
                                var badge = isComparing ? "Сравнение"
                                    : isSource ? "Взята строка"
                                    : headRow >= 0 ? "Чтение" : "";
                                var active = isComparing || isSource;
                                inputViews[i] = BuildTapeView($"Лента {i + 1}", badge, inputTapes[i], active,
                                    entry =>
                                    {
                                        if (isSource && entry.RowNumber == lastSourceRow)
                                            return "Source";
                                        if (headRow >= 0 && entry.RowNumber == headRow)
                                            return isComparing ? "Compare" : "Head";
                                        if (consumedRows.Contains(entry.RowNumber))
                                            return "Consumed";
                                        return "Normal";
                                    });
                            }

                            outputViews = new TapeView[tapeCount];
                            for (var i = 0; i < tapeCount; i++)
                            {
                                var isTarget = !isComplete && frame.Tape == i;
                                var badge = isComplete && i == 0 ? "Отсортировано"
                                    : isTarget ? $"Запись · С{frame.Run}" : "";
                                var tapeIndex = i;
                                outputViews[i] = BuildTapeView($"Лента {i + 1}", badge, outputTapes[i], isTarget || (isComplete && i == 0),
                                    entry =>
                                    {
                                        if (frame.Step.Kind == StepKind.Write && isTarget && entry.RowNumber == lastWrittenRow)
                                            return "Added";
                                        if (isComplete && tapeIndex == 0)
                                            return "Head";
                                        return "Normal";
                                    });
                            }
                        }

                        Pass.Text = isComplete
                            ? $"Готово · проходов {passNumber}"
                            : passNumber == 0 ? "Разбиение на серии" : $"Проход слияния {passNumber}";
                        Stage.Text = stageText;
                        Explanation.Text = messageText;
                        InputSection.Visibility = Visibility.Visible;
                        InputSectionTitle.Text = inputTitle;
                        OutputSectionTitle.Text = outputTitle;
                        FlowCaption.Text = flowText;
                        InputTapeList.ItemsSource = inputViews;
                        OutputTapeList.ItemsSource = outputViews;
                        EmptyPreview.Visibility = previewSnapshot.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                        Preview.ItemsSource = previewSnapshot;
                    });
                }, token);
            }, result =>
            {
                Result.Text = $"Сохранено строк {result.RowCount} · исходных серий {result.InitialRuns} · проходов {result.Passes}\n{save.FileName}";
                Result.ToolTip = save.FileName;
            });
        }
        finally
        {
            Inputs.IsEnabled = true;
        }
    }

    private static TapeBuffer[] CreateTapeBuffers(int count) =>
        Enumerable.Range(0, count).Select(_ => new TapeBuffer()).ToArray();

    private static TapeEntry CreateEntry(TableRow row, int keyColumn) =>
        new(row.Number, row.Fields[keyColumn], $"Строка №{row.Number}: {string.Join("  |  ", row.Fields)}");

    private static void AppendToTape(TapeBuffer tape, TapeEntry entry, int runNumber, bool startsNewRun)
    {
        if (startsNewRun || tape.Runs.Count == 0)
            tape.Runs.Add(new RunBuffer(runNumber));
        tape.Runs[^1].Entries.Add(entry);
        tape.TotalCount++;
    }

    private static long LastRowNumber(TapeBuffer tape) =>
        tape.Runs.Count > 0 && tape.Runs[^1].Entries.Count > 0 ? tape.Runs[^1].Entries[^1].RowNumber : -1L;

    private static TapeView BuildTapeView(string name, string badge, TapeBuffer tape, bool active,
        Func<TapeEntry, string> stateOf)
    {
        var summary = tape.TotalCount == 0
            ? "0 эл."
            : $"{tape.TotalCount} эл. · {tape.Runs.Count} сер.";
        if (tape.Runs.Count == 0)
            return new(name, badge, summary, [], active, "Пока пусто");

        var runs = new List<RunView>();
        var shown = 0;
        var skipped = 0;
        foreach (var run in tape.Runs)
        {
            if (shown >= MaxVisibleItemsPerTape)
            {
                skipped += run.Entries.Count;
                continue;
            }

            var take = Math.Min(run.Entries.Count, MaxVisibleItemsPerTape - shown);
            var items = new TapeItemView[take];
            for (var i = 0; i < take; i++)
            {
                var entry = run.Entries[i];
                items[i] = new(entry.KeyText, entry.Tooltip, stateOf(entry));
            }

            runs.Add(new($"С{run.Number}", items));
            shown += take;
            skipped += run.Entries.Count - take;
        }

        if (skipped > 0)
            runs.Add(new("…", [new($"+{skipped} эл.", $"Скрыто элементов для скорости отрисовки: {skipped}", "Consumed")]));

        return new(name, badge, summary, runs.ToArray(), active, "Пока пусто");
    }

    private static RunView BuildRunView(string label, List<TapeEntry> entries, Func<TapeEntry, string> stateOf)
    {
        var start = Math.Max(0, entries.Count - MaxVisibleItemsPerTape);
        var items = new List<TapeItemView>(entries.Count - start + (start > 0 ? 1 : 0));
        if (start > 0)
            items.Add(new($"+{start} ранее", $"Показаны последние {MaxVisibleItemsPerTape} строк потока", "Consumed"));

        for (var i = start; i < entries.Count; i++)
        {
            var entry = entries[i];
            items.Add(new(entry.KeyText, entry.Tooltip, stateOf(entry)));
        }

        return new(label, items.ToArray());
    }

    public Task CloseAsync() => Playback.CloseAsync();

    private sealed record TapeEntry(long RowNumber, string KeyText, string Tooltip);
    private sealed class RunBuffer(int number)
    {
        public int Number { get; } = number;
        public List<TapeEntry> Entries { get; } = [];
    }
    private sealed class TapeBuffer
    {
        public List<RunBuffer> Runs { get; } = [];
        public int TotalCount { get; set; }
    }

    private sealed record TapeItemView(string KeyText, string Tooltip, string State);
    private sealed record RunView(string Label, TapeItemView[] Items);
    private sealed record TapeView(string Name, string Badge, string Summary, RunView[] Runs, bool Active, string EmptyText)
    {
        public bool IsEmpty => Runs.Length == 0;
    }
}
