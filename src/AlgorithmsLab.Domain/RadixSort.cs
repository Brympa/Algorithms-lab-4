namespace AlgorithmsLab.Domain;

public sealed class RadixSort
{
    public void Sort(string[] words, SortObserver<string>? observer, CancellationToken cancellationToken)
    {
        if (words.Length < 2)
            return;

        var maximumLength = words.Max(word => word.Length);
        var target = new string[words.Length];

        // Ноль означает конец слова. Любая кодовая единица UTF-16 получает ключ от 1 до 65536.
        // Два устойчивых прохода по байтам дают порядок Ordinal и ставят префиксы перед длинными словами.
        var counts = new int[257];
        for (var position = maximumLength - 1; position >= 0; position--)
        {
            for (var shift = 0; shift <= 8; shift += 8)
            {
                cancellationToken.ThrowIfCancellationRequested();
                observer?.Invoke(new(StepKind.Phase,
                    $"Смотрим на символ №{position + 1}. Раскладываем слова по корзинам по части кода символа.",
                    Label: $"Символ №{position + 1} · часть {shift / 8 + 1}/2"), words);

                Array.Clear(counts);
                foreach (var word in words)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    counts[Bucket(word, position, shift)]++;
                }

                var offset = 0;
                for (var bucket = 0; bucket < counts.Length; bucket++)
                    (counts[bucket], offset) = (offset, offset + counts[bucket]);

                for (var index = 0; index < words.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var word = words[index];
                    var bucket = Bucket(word, position, shift);
                    var destination = counts[bucket]++;
                    target[destination] = word;
                    observer?.Invoke(new(StepKind.Write,
                        $"«{word}» → корзина {bucket}, позиция {destination + 1}.", index), words);
                }

                Array.Copy(target, words, words.Length);
                observer?.Invoke(new(StepKind.Phase, "Собрали корзины по порядку и получили новую очередь слов.",
                    Label: "Корзины собраны"), words);
            }
        }
    }

    private static int Bucket(string word, int position, int shift)
    {
        var key = position < word.Length ? word[position] + 1 : 0;
        return shift == 0 ? key & 255 : key >> 8;
    }
}
