using System.Globalization;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Application;

public sealed class ArraySorting
{
    public static int[] Parse(string input)
    {
        var tokens = input.Split([' ', '\t', '\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Select(token => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : throw new FormatException($"«{token}» не является целым числом.")).ToArray();
    }

    public int[] Run(int[] values, IComparisonSort algorithm, SortObserver<int>? observer, CancellationToken token)
    {
        var items = (int[])values.Clone();
        observer?.Invoke(new(StepKind.Start, "Это исходный порядок чисел.", Label: "Исходный массив"), items);
        algorithm.Sort(new SortBuffer<int>(items, Comparer<int>.Default, observer, token));
        token.ThrowIfCancellationRequested();
        observer?.Invoke(new(StepKind.Complete, "Готово! Числа расположены от меньшего к большему.", Label: "Готово"), items);
        return items;
    }
}
