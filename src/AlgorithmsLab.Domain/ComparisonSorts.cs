namespace AlgorithmsLab.Domain;

public interface IComparisonSort
{
    void Sort<T>(SortBuffer<T> items);
}

public sealed class SelectSort : IComparisonSort
{
    public void Sort<T>(SortBuffer<T> items)
    {
        for (var first = 0; first < items.Count - 1; first++)
        {
            items.Phase("Ищем самое маленькое число среди оставшихся.", first, items.Count - 1, "Поиск минимума", first);
            var minimum = first;
            for (var next = first + 1; next < items.Count; next++)
                if (items.Compare(next, minimum) < 0)
                    minimum = next;

            if (minimum != first)
                items.Swap(first, minimum);
        }
    }
}

public sealed class InsertSort : IComparisonSort
{
    public void Sort<T>(SortBuffer<T> items)
    {
        for (var next = 1; next < items.Count; next++)
        {
            items.Phase($"Берём {items[next]} и перемещаем его влево на своё место.",
                0, next, $"Вставляем {items[next]}", next);
            var position = next;
            while (position > 0 && items.Compare(position - 1, position) > 0)
            {
                items.Swap(position - 1, position);
                position--;
            }
        }
    }
}

public sealed class HeapSort : IComparisonSort
{
    public void Sort<T>(SortBuffer<T> items)
    {
        if (items.Count < 2)
            return;

        items.Phase("Строим кучу: число наверху каждой ветки не меньше чисел под ним.",
            0, items.Count - 1, "Построение кучи");
        for (var root = items.Count / 2 - 1; root >= 0; root--)
            SiftDown(items, root, items.Count);

        for (var end = items.Count - 1; end > 0; end--)
        {
            items.Phase("Самое большое число наверху кучи. Перенесём его в конец активной части.",
                0, end, "Вершина кучи", 0);
            items.Swap(0, end);
            items.Phase("Последнее число уже на месте. Восстанавливаем кучу слева.",
                0, end - 1, "Восстановление кучи");
            SiftDown(items, 0, end);
        }
    }

    private static void SiftDown<T>(SortBuffer<T> items, int root, int count)
    {
        while (root < count / 2)
        {
            var child = 2 * root + 1;
            if (child + 1 < count && items.Compare(child, child + 1) < 0)
                child++;

            if (items.Compare(root, child) >= 0)
                return;

            items.Swap(root, child);
            root = child;
        }
    }
}

public sealed class QuickSort : IComparisonSort
{
    public void Sort<T>(SortBuffer<T> items)
    {
        try
        {
            SortRange(items, 0, items.Count - 1);
        }
        finally
        {
            items.PivotIndex = -1;
        }
    }

    private static void SortRange<T>(SortBuffer<T> items, int left, int right)
    {
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            var pivot = items[middle];
            items.PivotIndex = middle;
            items.Phase($"Берём {pivot} за опорный элемент (Pivot). Меньшие значения отправляем влево, большие — вправо.",
                left, right, $"Опора (Pivot) · {pivot}", middle);

            // Указатели Хоара сходятся навстречу друг другу.
            var lower = left - 1;
            var upper = right + 1;
            while (true)
            {
                do { lower++; } while (items.Compare(lower, pivot, "опора") < 0);
                do { upper--; } while (items.Compare(upper, pivot, "опора") > 0);
                if (lower >= upper)
                    break;

                items.Swap(lower, upper);
            }

            items.PivotIndex = -1;

            // Рекурсивно обрабатываем меньшую часть, чтобы ограничить глубину стека.
            if (upper - left < right - upper - 1)
            {
                SortRange(items, left, upper);
                left = upper + 1;
            }
            else
            {
                SortRange(items, upper + 1, right);
                right = upper;
            }
        }
    }
}
