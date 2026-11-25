using System.Windows;
using System.Windows.Controls;
using AlgorithmsLab.Application;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Desktop;

public partial class ArrayView : UserControl
{
    private const int MinimumGenerated = 4;
    private const int MaximumGenerated = 256;
    private const int MaximumLength = MaximumGenerated;
    private readonly ArraySorting sorting = new();

    private readonly MethodChoice[] choices =
    [
        new("SelectSort · выбором", "Сортировка выбором", "O(n²)",
            "Находим самое маленькое из оставшихся чисел и ставим его на первое свободное место.", new SelectSort()),
        new("InsertSort · вставками", "Сортировка вставками", "O(n²)",
            "Берём следующее число и перемещаем его влево с помощью обменов на своё место среди уже упорядоченных чисел.", new InsertSort()),
        new("HeapSort · пирамидальная", "Пирамидальная сортировка", "O(n log n)",
            "Строим кучу с самым большим числом наверху. Переносим это число в конец и повторяем.", new HeapSort()),
        new("QuickSort · Хоара", "Быстрая сортировка Хоара", "O(n log n) в среднем",
            "Выбираем опорный элемент (Pivot), переносим меньшие числа влево, большие вправо и повторяем для каждой части.", new QuickSort())
    ];

    public ArrayView()
    {
        InitializeComponent();
        Method.ItemsSource = choices;
        Method.SelectedIndex = 0;
    }

    private void SelectMethod(object sender, SelectionChangedEventArgs e)
    {
        if (Method.SelectedItem is not MethodChoice choice)
            return;

        ChartTitle.Text = choice.Title;
        Complexity.Text = choice.Complexity;
        Stage.Text = "Как это работает";
        Explanation.Text = choice.Explanation;
        UpdatePreview();
    }

    private void PreviewNumbers(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (Chart is null || Numbers is null)
            return;

        try
        {
            var values = ArraySorting.Parse(Numbers.Text);
            if (values.Length <= MaximumLength)
                Chart.Draw(values, new(StepKind.Start, ""));
        }
        catch (FormatException)
        {
            // Незаконченный ввод не мешает редактированию.
        }
    }

    private void Generate(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Count.Text, out var count) || count is < MinimumGenerated or > MaximumGenerated)
        {
            Playback.ShowStatus($"Введите количество элементов от {MinimumGenerated} до {MaximumGenerated}.", error: true);
            return;
        }

        Numbers.Text = string.Join("  ", Enumerable.Range(0, count).Select(_ => Random.Shared.Next(10, 100)));
        Playback.ShowStatus($"Новый массив готов · элементов {count}");
    }

    private async void Start(object sender, RoutedEventArgs e)
    {
        int[] input;
        try
        {
            input = ArraySorting.Parse(Numbers.Text);
            if (input.Length is < 1 or > MaximumLength)
                throw new FormatException($"Введите от 1 до {MaximumLength} целых чисел.");
        }
        catch (FormatException error)
        {
            Playback.ShowStatus(error.Message, error: true);
            return;
        }

        var choice = (MethodChoice)Method.SelectedItem;
        Inputs.IsEnabled = false;
        try
        {
            await Playback.RunAsync((report, token) => sorting.Run(input, choice.Algorithm, (step, values) =>
                report(step, () =>
                {
                    Chart.Draw(values, step, Playback.CurrentAnimationMs);
                    Stage.Text = step.Label ?? Stage.Text;
                    Explanation.Text = step.Message;
                }), token));
        }
        finally
        {
            Inputs.IsEnabled = true;
        }
    }

    public void RefreshTheme() => Chart.InvalidateVisual();
    public Task CloseAsync() => Playback.CloseAsync();

    private sealed record MethodChoice(string Menu, string Title, string Complexity, string Explanation, IComparisonSort Algorithm)
    {
        public override string ToString() => Menu;
    }
}
