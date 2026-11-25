namespace AlgorithmsLab.Domain;

public sealed class SortBuffer<T>(
    T[] values, IComparer<T> comparer, SortObserver<T>? observer, CancellationToken cancellationToken)
{
    public int Count => values.Length;
    public T this[int index] => values[index];
    public int PivotIndex { get; set; } = -1;

    public int Compare(int first, int second)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = comparer.Compare(values[first], values[second]);
        observer?.Invoke(new(StepKind.Compare, $"Сравниваем {values[first]} и {values[second]}.",
            first, second, Pivot: PivotIndex), values);
        return result;
    }

    public int Compare(int index, T key, string keyLabel)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = comparer.Compare(values[index], key);
        observer?.Invoke(new(StepKind.Compare, $"Сравниваем {values[index]} и {key} ({keyLabel}).",
            index, Pivot: PivotIndex), values);
        return result;
    }

    public void Swap(int first, int second)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var message = observer is null ? null : $"Меняем местами {values[first]} и {values[second]}.";
        (values[first], values[second]) = (values[second], values[first]);
        if (PivotIndex == first)
            PivotIndex = second;
        else if (PivotIndex == second)
            PivotIndex = first;
        observer?.Invoke(new(StepKind.Swap, message!, first, second, Pivot: PivotIndex), values);
    }

    public void Write(int index, T value, int source, string reason)
    {
        cancellationToken.ThrowIfCancellationRequested();
        values[index] = value;
        observer?.Invoke(new(StepKind.Write, $"{reason} {value} → позиция {index + 1}.",
            index, source, Pivot: PivotIndex), values);
    }

    public void Phase(string message, int start, int end, string label, int focus = -1)
    {
        cancellationToken.ThrowIfCancellationRequested();
        observer?.Invoke(new(StepKind.Phase, message, focus, RangeStart: start, RangeEnd: end,
            Label: label, Pivot: PivotIndex), values);
    }
}
