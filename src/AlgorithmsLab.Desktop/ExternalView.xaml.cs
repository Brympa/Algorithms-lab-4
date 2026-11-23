using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlgorithmsLab.Application;
using AlgorithmsLab.Infrastructure;
using Microsoft.Win32;

namespace AlgorithmsLab.Desktop;

public partial class ExternalView : UserControl
{
    private readonly ExternalSorting sorting = new(new CsvTableStorage());
    private string? source;
    private string[] header = [];

    public ExternalView()
    {
        InitializeComponent();
        Playback.Delay = 100;
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
            MergeMethod.Natural => "Сначала найдём группы соседних строк, которые уже идут по порядку. Потом будем соединять группы попарно.",
            MergeMethod.Multiway => "Распределим уже упорядоченные группы по четырём лентам и будем соединять до четырёх групп сразу.",
            _ => "Каждая строка сначала образует свою группу. Соединяем соседние группы попарно, пока не останется одна."
        };

        var count = Method.SelectedIndex == (int)MergeMethod.Multiway ? 4 : 2;
        TapeList.ItemsSource = Enumerable.Range(1, count).Select(number => new TapeTile(number, "Пусто", false)).ToArray();
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
                var latest = Enumerable.Repeat("Пусто", options.Method == MergeMethod.Multiway ? 4 : 2).ToArray();
                var tail = new Queue<string>();
                var previousPass = 0;
                var stage = "Разбиение на серии";
                return sorting.Run(options, frame =>
                {
                    stage = frame.Step.Label ?? stage;
                    if (frame.Pass != previousPass)
                    {
                        Array.Fill(latest, "Пусто");
                        tail.Clear();
                        previousPass = frame.Pass;
                    }

                    if (frame.Row is { } row)
                    {
                        latest[frame.Tape] = $"Серия {frame.Run} · {row.Fields[options.KeyColumn]}";
                        if (frame.WritesMergedRow)
                        {
                            if (tail.Count == 5)
                                tail.Dequeue();
                            tail.Enqueue($"Строка {row.Number}  ·  {string.Join("  |  ", row.Fields)}");
                        }
                    }

                    report(frame.Step, () =>
                    {
                        Pass.Text = frame.Pass == 0 ? "Разбиение" : $"Проход {frame.Pass}";
                        Stage.Text = stage;
                        Explanation.Text = frame.Step.Message;
                        TapeList.ItemsSource = latest.Select((value, index) =>
                            new TapeTile(index + 1, value, frame.Tape == index)).ToArray();
                        EmptyPreview.Visibility = tail.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                        Preview.ItemsSource = tail.Reverse().ToArray();
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

    public Task CloseAsync() => Playback.CloseAsync();

    private sealed record TapeTile(int Number, string LastWrite, bool Active)
    {
        public string Name => $"Лента {Number}";
    }
}
