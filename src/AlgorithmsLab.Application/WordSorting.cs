using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Application;

public enum WordMethod { Quick, Radix }

public sealed record WordCount(string Word, int Count);
public sealed record WordTiming(int WordCount, double QuickMilliseconds, double RadixMilliseconds);

public sealed partial class WordSorting(IComparisonSort quickSort, RadixSort radixSort)
{
    public const int BenchmarkRepeats = 7;
    public const int BenchmarkSeed = 42;
    public static IReadOnlyList<int> BenchmarkSizes { get; } = Array.AsReadOnly(new[] { 100, 500, 1000, 2000, 5000 });

    public static string[] Tokenize(string text) =>
        WordPattern().Matches(text.Normalize(NormalizationForm.FormC)).Select(match => match.Value.ToLowerInvariant()).ToArray();

    public IReadOnlyList<WordCount> Run(string[] words, WordMethod method, SortObserver<string>? observer,
        CancellationToken token)
    {
        var sorted = (string[])words.Clone();
        observer?.Invoke(new(StepKind.Start, $"Выделили слов: {words.Length}.", Label: "Исходный порядок"), sorted);
        Sort(sorted, method, observer, token);

        var result = new List<WordCount>();
        for (var start = 0; start < sorted.Length;)
        {
            token.ThrowIfCancellationRequested();
            var end = start + 1;
            while (end < sorted.Length && StringComparer.Ordinal.Equals(sorted[start], sorted[end]))
                end++;

            result.Add(new(sorted[start], end - start));
            observer?.Invoke(new(StepKind.Phase, $"Слово «{sorted[start]}» встречается {end - start} раз.",
                start, RangeStart: start, RangeEnd: end - 1, Label: "Подсчёт слов"), sorted);
            start = end;
        }

        token.ThrowIfCancellationRequested();
        observer?.Invoke(new(StepKind.Complete, $"Подсчёт завершён. Разных слов: {result.Count}.", Label: "Готово"), sorted);
        return result;
    }

    public IReadOnlyList<WordTiming> Benchmark(string[] corpus, CancellationToken token)
    {
        if (corpus.Length == 0)
            throw new ArgumentException("Для замеров нужен текст хотя бы с одним словом.", nameof(corpus));

        var random = new Random(BenchmarkSeed);
        var input = new string[BenchmarkSizes[^1]];
        for (var i = 0; i < input.Length; i++)
            input[i] = corpus[random.Next(corpus.Length)];

        // Прогрев на полном наборе уменьшает вклад JIT в короткие измерения.
        Sort((string[])input.Clone(), WordMethod.Quick, null, token);
        Sort((string[])input.Clone(), WordMethod.Radix, null, token);

        var timings = new List<WordTiming>();
        foreach (var size in BenchmarkSizes)
        {
            var sample = input[..size];
            var quickTimes = new double[BenchmarkRepeats];
            var radixTimes = new double[BenchmarkRepeats];
            for (var repeat = 0; repeat < BenchmarkRepeats; repeat++)
            {
                if (repeat % 2 == 0)
                {
                    quickTimes[repeat] = Measure(sample, WordMethod.Quick, token);
                    radixTimes[repeat] = Measure(sample, WordMethod.Radix, token);
                }
                else
                {
                    radixTimes[repeat] = Measure(sample, WordMethod.Radix, token);
                    quickTimes[repeat] = Measure(sample, WordMethod.Quick, token);
                }
            }

            Array.Sort(quickTimes);
            Array.Sort(radixTimes);
            timings.Add(new(size, quickTimes[BenchmarkRepeats / 2], radixTimes[BenchmarkRepeats / 2]));
        }

        return timings;
    }

    private double Measure(string[] sample, WordMethod method, CancellationToken token)
    {
        var copy = (string[])sample.Clone();
        var start = Stopwatch.GetTimestamp();
        Sort(copy, method, null, token);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void Sort(string[] words, WordMethod method, SortObserver<string>? observer, CancellationToken token)
    {
        switch (method)
        {
            case WordMethod.Quick:
                quickSort.Sort(new SortBuffer<string>(words, StringComparer.Ordinal, observer, token));
                break;
            case WordMethod.Radix:
                radixSort.Sort(words, observer, token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method));
        }
    }

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}]*(?:['’]\p{L}[\p{L}\p{M}]*)*")]
    private static partial Regex WordPattern();
}
