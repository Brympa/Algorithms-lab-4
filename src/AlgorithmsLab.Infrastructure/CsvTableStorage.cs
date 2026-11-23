using System.Text;
using AlgorithmsLab.Application;
using Microsoft.VisualBasic.FileIO;

namespace AlgorithmsLab.Infrastructure;

public sealed class CsvTableStorage : IExternalTableStorage
{
    public string[] ReadHeader(string path, TableFormat format)
    {
        using var parser = OpenCsv(path, format);
        return ReadHeader(parser);
    }

    public IExternalWorkspace Open(ExternalSortOptions options)
    {
        if (string.Equals(Path.GetFullPath(options.Source), Path.GetFullPath(options.Destination),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Выберите для результата другой файл.");

        return new Workspace(options, ReadHeader(options.Source, options.Format));
    }

    private static TextFieldParser OpenCsv(string path, TableFormat format)
    {
        var parser = new TextFieldParser(path, new UTF8Encoding(false, true), detectEncoding: true)
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(format.Separator.ToString());
        return parser;
    }

    private static string[] ReadHeader(TextFieldParser parser) =>
        ReadFields(parser) ?? throw new FormatException("Файл пуст. В первой строке нужны названия столбцов.");

    private static string[]? ReadFields(TextFieldParser parser)
    {
        try
        {
            return parser.ReadFields();
        }
        catch (MalformedLineException exception)
        {
            throw new FormatException($"Ошибка формата таблицы около строки {exception.LineNumber}.", exception);
        }
    }

    private sealed class Workspace(ExternalSortOptions options, string[] header) : IExternalWorkspace
    {
        private readonly string directory = Directory.CreateTempSubdirectory("AlgorithmsLab-").FullName;
        private int nextTape;

        public IReadOnlyList<string> Header => header;

        public IEnumerable<TableRow> ReadSource(CancellationToken token)
        {
            using var parser = OpenCsv(options.Source, options.Format);
            if (!ReadHeader(parser).SequenceEqual(header))
                throw new IOException("Заголовок исходного файла изменился. Откройте таблицу ещё раз.");

            for (var number = 1L; !parser.EndOfData; number++)
            {
                token.ThrowIfCancellationRequested();
                var fields = ReadFields(parser)!;
                if (fields.Length != header.Length)
                    throw new FormatException($"Запись №{number}: ожидалось столбцов {header.Length}, получено {fields.Length}.");

                yield return new(number, fields);
            }
        }

        public int CreateTape() => nextTape++;
        public ITapeReader OpenReader(int tape) => new TapeReader(TapePath(tape), header.Length);
        public ITapeWriter OpenWriter(int tape) => new TapeWriter(TapePath(tape));
        public void DeleteTape(int tape) => File.Delete(TapePath(tape));

        public void SaveResult(int tape, CancellationToken token)
        {
            var destination = Path.GetFullPath(options.Destination);
            var staging = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.tmp");
            try
            {
                using (var writer = new StreamWriter(staging, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                using (var reader = OpenReader(tape))
                {
                    WriteLine(writer, header);
                    while (reader.HasRun)
                    {
                        while (reader.ReadRow() is { } row)
                        {
                            token.ThrowIfCancellationRequested();
                            WriteLine(writer, row.Fields);
                        }
                    }
                }

                token.ThrowIfCancellationRequested();
                File.Move(staging, destination, overwrite: true);
            }
            finally
            {
                File.Delete(staging);
            }
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);

        private string TapePath(int tape) => Path.Combine(directory, $"{tape}.bin");

        private void WriteLine(TextWriter writer, string[] fields)
        {
            var separator = options.Format.Separator;
            // Кавычки защищают разделители, пробелы и переводы строк внутри поля.
            writer.WriteLine(string.Join(separator, fields.Select(value => $"\"{value.Replace("\"", "\"\"")}\"")));
        }
    }

    private sealed class TapeWriter(string path) : ITapeWriter
    {
        private readonly BinaryWriter writer = new(File.Create(path));

        public void Write(TableRow row)
        {
            writer.Write(true);
            writer.Write(row.Number);
            writer.Write(row.NumericKey.HasValue);
            if (row.NumericKey is { } number)
                writer.Write(number);

            foreach (var field in row.Fields)
                writer.Write(field);
        }

        public void EndRun() => writer.Write(false);
        public void Dispose() => writer.Dispose();
    }

    private sealed class TapeReader(string path, int columns) : ITapeReader
    {
        private readonly BinaryReader reader = new(File.OpenRead(path));
        public bool HasRun => reader.BaseStream.Position < reader.BaseStream.Length;

        public TableRow? ReadRow()
        {
            if (!reader.ReadBoolean())
                return null;

            var number = reader.ReadInt64();
            var key = reader.ReadBoolean() ? reader.ReadDecimal() : (decimal?)null;
            var fields = new string[columns];
            for (var column = 0; column < fields.Length; column++)
                fields[column] = reader.ReadString();

            return new(number, fields, key);
        }

        public void Dispose() => reader.Dispose();
    }
}
