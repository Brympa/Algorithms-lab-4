namespace AlgorithmsLab.Domain;

public sealed class RadixSort
{
    private const int DirectRangeLimit = 2048;

    public void Sort(string[] words, SortObserver<string>? observer, CancellationToken cancellationToken)
    {
        if (words.Length < 2)
            return;

        var maximumLength = 0;
        var minChar = int.MaxValue;
        var maxChar = -1;
        foreach (var word in words)
        {
            if (word.Length > maximumLength)
                maximumLength = word.Length;
            foreach (var ch in word)
            {
                if (ch < minChar)
                    minChar = ch;
                if (ch > maxChar)
                    maxChar = ch;
            }
        }

        if (maximumLength == 0)
            return;

        var target = new string[words.Length];
        if (maxChar - minChar < DirectRangeLimit)
            SortByCharacter(words, target, maximumLength, minChar, maxChar, observer, cancellationToken);
        else
            SortByBytes(words, target, maximumLength, observer, cancellationToken);
    }

    private static void SortByCharacter(
        string[] words, string[] target, int maximumLength, int minChar, int maxChar,
        SortObserver<string>? observer, CancellationToken cancellationToken)
    {
        // Ноль означает конец слова, 1..(maxChar-minChar+1) — код символа со сдвигом.
        // Сохраняет порядок StringComparer.Ordinal за один устойчивый проход на позицию символа.
        var counts = new int[maxChar - minChar + 2];
        for (var position = maximumLength - 1; position >= 0; position--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observer?.Invoke(new(StepKind.Phase,
                $"Смотрим на символ №{position + 1} (справа налево). Раскладываем слова по корзинам-лентам.",
                Label: $"Символ №{position + 1} из {maximumLength}",
                CharPosition: position), words);

            Array.Clear(counts);
            foreach (var word in words)
            {
                cancellationToken.ThrowIfCancellationRequested();
                counts[CharBucket(word, position, minChar)]++;
            }

            var offset = 0;
            for (var bucket = 0; bucket < counts.Length; bucket++)
                (counts[bucket], offset) = (offset, offset + counts[bucket]);

            for (var index = 0; index < words.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var word = words[index];
                var bucket = CharBucket(word, position, minChar);
                var destination = counts[bucket]++;
                target[destination] = word;
                if (observer is not null)
                {
                    var label = bucket == 0 ? "∅" : ((char)(minChar + bucket - 1)).ToString();
                    var message = bucket == 0
                        ? $"«{word}» (короче {position + 1} симв.) → корзина ∅, позиция {destination + 1}."
                        : $"«{word}» (символ «{label}») → корзина «{label}», позиция {destination + 1}.";
                    observer.Invoke(new(StepKind.Write, message,
                        First: index, Second: destination, CharPosition: position, BucketLabel: label), words);
                }
            }

            Array.Copy(target, words, words.Length);
            observer?.Invoke(new(StepKind.Phase,
                $"Собрали корзины по порядку после символа №{position + 1} и получили новую ленту слов.",
                Label: $"Корзины собраны · символ №{position + 1}",
                CharPosition: position), words);
        }
    }

    private static void SortByBytes(
        string[] words, string[] target, int maximumLength,
        SortObserver<string>? observer, CancellationToken cancellationToken)
    {
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
                    Label: $"Символ №{position + 1} · часть {shift / 8 + 1}/2",
                    CharPosition: position), words);

                Array.Clear(counts);
                foreach (var word in words)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    counts[ByteBucket(word, position, shift)]++;
                }

                var offset = 0;
                for (var bucket = 0; bucket < counts.Length; bucket++)
                    (counts[bucket], offset) = (offset, offset + counts[bucket]);

                for (var index = 0; index < words.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var word = words[index];
                    var bucket = ByteBucket(word, position, shift);
                    var destination = counts[bucket]++;
                    target[destination] = word;
                    if (observer is not null)
                    {
                        var label = bucket == 0 && shift == 0 ? "∅" : $"#{bucket:D3}";
                        observer.Invoke(new(StepKind.Write,
                            $"«{word}» → корзина {label}, позиция {destination + 1}.",
                            First: index, Second: destination, CharPosition: position, BucketLabel: label), words);
                    }
                }

                Array.Copy(target, words, words.Length);
                observer?.Invoke(new(StepKind.Phase,
                    "Собрали корзины по порядку и получили новую очередь слов.",
                    Label: "Корзины собраны",
                    CharPosition: position), words);
            }
        }
    }

    private static int CharBucket(string word, int position, int minChar) =>
        position < word.Length ? word[position] - minChar + 1 : 0;

    private static int ByteBucket(string word, int position, int shift)
    {
        var key = position < word.Length ? word[position] + 1 : 0;
        return shift == 0 ? key & 255 : key >> 8;
    }
}
