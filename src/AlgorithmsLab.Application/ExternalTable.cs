using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Application;

public enum MergeMethod { Direct, Natural, Multiway }
public enum ColumnType { Text, Number }

public sealed record TableFormat(char Separator);
public sealed record TableRow(long Number, string[] Fields, decimal? NumericKey = null);
public sealed record TableFilter(int Column, string Value);
public sealed record ExternalSortOptions(string Source, string Destination, TableFormat Format,
    int KeyColumn, ColumnType KeyType, MergeMethod Method, TableFilter? Filter = null);
public sealed record ExternalSortResult(long RowCount, int InitialRuns, int Passes);

public sealed record MergeStep(
    SortStep Step,
    int Pass,
    int TapeCount,
    int Tape = -1,
    int Run = 0,
    TableRow? Row = null,
    bool WritesMergedRow = false,
    int SourceTape = -1,
    int CompareTapeA = -1,
    int CompareTapeB = -1,
    long[]? HeadRows = null,
    bool StartsNewRun = false);

public interface IExternalTableStorage
{
    string[] ReadHeader(string path, TableFormat format);
    IExternalWorkspace Open(ExternalSortOptions options);
}

public interface IExternalWorkspace : IDisposable
{
    IReadOnlyList<string> Header { get; }
    IEnumerable<TableRow> ReadSource(CancellationToken token);
    int CreateTape();
    ITapeReader OpenReader(int tape);
    ITapeWriter OpenWriter(int tape);
    void DeleteTape(int tape);
    void SaveResult(int tape, CancellationToken token);
}

public interface ITapeReader : IDisposable
{
    // Доступно только на границе серий, до первого чтения или после ReadRow() == null.
    bool HasRun { get; }
    TableRow? ReadRow();
}

public interface ITapeWriter : IDisposable
{
    void Write(TableRow row);
    void EndRun();
}
