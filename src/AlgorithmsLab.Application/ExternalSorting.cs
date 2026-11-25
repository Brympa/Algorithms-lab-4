using System.Globalization;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Application;

public sealed class ExternalSorting(IExternalTableStorage storage)
{
    private static readonly CompareInfo Russian = CultureInfo.GetCultureInfo("ru-RU").CompareInfo;
    private const NumberStyles NumericStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public string[] ReadHeader(string path, TableFormat format) => storage.ReadHeader(path, format);

    public ExternalSortResult Run(ExternalSortOptions options, Action<MergeStep>? observer, CancellationToken token)
    {
        using var work = storage.Open(options);
        if (options.KeyColumn < 0 || options.KeyColumn >= work.Header.Count)
            throw new ArgumentOutOfRangeException(nameof(options), "Выберите столбец из заголовка таблицы.");
        if (options.Filter is { } filter && (filter.Column < 0 || filter.Column >= work.Header.Count))
            throw new ArgumentOutOfRangeException(nameof(options), "Выберите столбец для фильтра.");

        var tapeCount = options.Method == MergeMethod.Multiway ? 4 : 2;
        var tapes = CreateTapes(work, tapeCount);
        var pass = 0;
        var run = 0;
        var rows = 0L;
        Publish(StepKind.Start, "Читаем строки из файла и раскладываем их по временным лентам.",
            label: "Разбиение на серии");

        using (var writers = new Handles<ITapeWriter>(tapes, work.OpenWriter))
        {
            TableRow? previous = null;
            var tape = 0;
            foreach (var source in work.ReadSource(token))
            {
                if (options.Filter is { } condition &&
                    Russian.Compare(source.Fields[condition.Column].Trim(), condition.Value.Trim(), CompareOptions.IgnoreCase) != 0)
                    continue;

                var row = Prepare(source);
                var startsNewRun = previous is null || options.Method == MergeMethod.Direct ||
                                   Compare(previous, row, tape: tape, incomingRow: row) > 0;
                if (startsNewRun)
                {
                    if (previous is not null)
                        writers[tape].EndRun();

                    tape = run % tapeCount;
                    run++;
                    Publish(StepKind.Phase, $"Начинаем серию {run} на ленте {tape + 1}.",
                        tape, row: row, label: "Новая серия");
                }

                Write(writers[tape], row, tape, merged: false, startsNewRun: startsNewRun);
                previous = row;
                rows++;
            }

            if (previous is not null)
                writers[tape].EndRun();
        }

        var initialRuns = run;
        while (run > 1)
        {
            pass++;
            run = 0;
            Publish(StepKind.Phase, $"Начинаем проход {pass}. Берём по одной серии с каждой входной ленты.",
                label: $"Слияние · проход {pass}");
            var output = CreateTapes(work, tapeCount);
            using (var readers = new Handles<ITapeReader>(tapes, work.OpenReader))
            using (var writers = new Handles<ITapeWriter>(output, work.OpenWriter))
            {
                // В памяти находится только головная строка каждой участвующей серии.
                var heads = new TableRow?[tapeCount];
                while (readers.Any(reader => reader.HasRun))
                {
                    for (var index = 0; index < tapeCount; index++)
                        heads[index] = readers[index].HasRun ? readers[index].ReadRow() : null;

                    var target = run % tapeCount;
                    run++;
                    Publish(StepKind.Phase,
                        $"Собираем серию {run} на ленте {target + 1}. Сравниваем первые строки открытых серий.",
                        target, label: $"Слияние · серия {run}", headRows: HeadSnapshot(heads));

                    var firstInRun = true;
                    for (var winner = FirstRow(heads); winner >= 0; winner = FirstRow(heads))
                    {
                        var chosen = heads[winner]!;
                        heads[winner] = readers[winner].ReadRow();
                        Write(writers[target], chosen, target, merged: true,
                            sourceTape: winner, startsNewRun: firstInRun, headRows: HeadSnapshot(heads));
                        firstInRun = false;
                    }

                    writers[target].EndRun();
                }
            }

            foreach (var tape in tapes)
                work.DeleteTape(tape);

            tapes = output;
        }

        token.ThrowIfCancellationRequested();
        work.SaveResult(tapes[0], token);
        observer?.Invoke(new(new(StepKind.Complete, $"Готово! Записано строк: {rows}.", Label: "Файл сохранён"),
            pass, tapeCount, Run: run));
        return new(rows, initialRuns, pass);

        TableRow Prepare(TableRow row)
        {
            if (options.KeyType == ColumnType.Text)
                return row;

            var value = row.Fields[options.KeyColumn].Trim().Replace(',', '.');
            if (decimal.TryParse(value, NumericStyles, CultureInfo.InvariantCulture, out var number))
                return row with { NumericKey = number };

            throw new FormatException($"Запись №{row.Number}: «{value}» в выбранном столбце не является числом.");
        }

        int Compare(TableRow left, TableRow right, int tape = -1, TableRow? incomingRow = null,
            int compareTapeA = -1, int compareTapeB = -1, long[]? headRows = null)
        {
            var first = left.Fields[options.KeyColumn];
            var second = right.Fields[options.KeyColumn];
            var order = options.KeyType == ColumnType.Number
                ? left.NumericKey!.Value.CompareTo(right.NumericKey!.Value)
                : Russian.Compare(first, second, CompareOptions.IgnoreCase);
            Publish(StepKind.Compare, $"Сравниваем «{first}» и «{second}» (строки {left.Number} и {right.Number}).",
                tape: tape, row: incomingRow, compareTapeA: compareTapeA, compareTapeB: compareTapeB, headRows: headRows);
            return order != 0 ? order : left.Number.CompareTo(right.Number);
        }

        int FirstRow(TableRow?[] heads)
        {
            var best = -1;
            for (var index = 0; index < heads.Length; index++)
            {
                if (heads[index] is { } candidate &&
                    (best < 0 || Compare(candidate, heads[best]!,
                        compareTapeA: best, compareTapeB: index, headRows: HeadSnapshot(heads)) < 0))
                    best = index;
            }

            return best;
        }

        void Write(ITapeWriter writer, TableRow row, int tape, bool merged,
            int sourceTape = -1, bool startsNewRun = false, long[]? headRows = null)
        {
            token.ThrowIfCancellationRequested();
            writer.Write(row);
            var sourceInfo = sourceTape >= 0 ? $" (с ленты {sourceTape + 1})" : "";
            Publish(StepKind.Write,
                $"Записываем строку {row.Number} («{row.Fields[options.KeyColumn]}») на ленту {tape + 1}{sourceInfo}.",
                tape, row, merged, sourceTape: sourceTape, headRows: headRows, startsNewRun: startsNewRun);
        }

        long[]? HeadSnapshot(TableRow?[] heads)
        {
            if (observer is null)
                return null;

            var snapshot = new long[heads.Length];
            for (var i = 0; i < heads.Length; i++)
                snapshot[i] = heads[i]?.Number ?? -1;
            return snapshot;
        }

        void Publish(StepKind kind, string message, int tape = -1,
            TableRow? row = null, bool merged = false, string? label = null,
            int sourceTape = -1, int compareTapeA = -1, int compareTapeB = -1,
            long[]? headRows = null, bool startsNewRun = false)
        {
            token.ThrowIfCancellationRequested();
            observer?.Invoke(new(new(kind, message, Label: label), pass, tapeCount, tape, run, row, merged,
                sourceTape, compareTapeA, compareTapeB, headRows, startsNewRun));
        }
    }

    private static int[] CreateTapes(IExternalWorkspace work, int count) =>
        Enumerable.Range(0, count).Select(_ => work.CreateTape()).ToArray();

    private sealed class Handles<T> : List<T>, IDisposable where T : IDisposable
    {
        public Handles(int[] tapes, Func<int, T> open)
        {
            try
            {
                foreach (var tape in tapes)
                    Add(open(tape));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var handle in this)
                handle.Dispose();
        }
    }
}
