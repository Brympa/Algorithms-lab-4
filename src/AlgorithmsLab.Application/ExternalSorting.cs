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
        Publish(StepKind.Start, "Читаем строки из файла и раскладываем их по временным файлам.",
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
                if (previous is null || options.Method == MergeMethod.Direct || Compare(previous, row) > 0)
                {
                    if (previous is not null)
                        writers[tape].EndRun();

                    tape = run % tapeCount;
                    run++;
                    Publish(StepKind.Phase, $"Начинаем серию {run} на ленте {tape + 1}.",
                        tape, label: "Новая серия");
                }

                Write(writers[tape], row, tape, merged: false);
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
            Publish(StepKind.Phase, $"Начинаем проход {pass}. Берём по одной серии с каждой ленты.",
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
                    Publish(StepKind.Phase, $"Собираем серию {run}. Сравниваем первые строки открытых серий.",
                        target, label: "Слияние серий");

                    for (var winner = FirstRow(heads); winner >= 0; winner = FirstRow(heads))
                    {
                        Write(writers[target], heads[winner]!, target, merged: true);
                        heads[winner] = readers[winner].ReadRow();
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

        int Compare(TableRow left, TableRow right)
        {
            var first = left.Fields[options.KeyColumn];
            var second = right.Fields[options.KeyColumn];
            var order = options.KeyType == ColumnType.Number
                ? left.NumericKey!.Value.CompareTo(right.NumericKey!.Value)
                : Russian.Compare(first, second, CompareOptions.IgnoreCase);
            Publish(StepKind.Compare, $"Сравниваем «{first}» и «{second}» (строки {left.Number} и {right.Number}).");
            return order != 0 ? order : left.Number.CompareTo(right.Number);
        }

        int FirstRow(TableRow?[] heads)
        {
            var best = -1;
            for (var index = 0; index < heads.Length; index++)
            {
                if (heads[index] is { } candidate && (best < 0 || Compare(candidate, heads[best]!) < 0))
                    best = index;
            }

            return best;
        }

        void Write(ITapeWriter writer, TableRow row, int tape, bool merged)
        {
            token.ThrowIfCancellationRequested();
            writer.Write(row);
            Publish(StepKind.Write, $"Записываем строку {row.Number} («{row.Fields[options.KeyColumn]}») на ленту {tape + 1}.",
                tape, row, merged);
        }

        void Publish(StepKind kind, string message, int tape = -1,
            TableRow? row = null, bool merged = false, string? label = null)
        {
            token.ThrowIfCancellationRequested();
            observer?.Invoke(new(new(kind, message, Label: label), pass, tapeCount, tape, run, row, merged));
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
