using System.IO;
using System.Text;
using AlgorithmsLab.Domain;

namespace AlgorithmsLab.Desktop;

internal sealed class StepJournal : IDisposable
{
    public const int PageSize = 6;
    private readonly FileStream file = new(
        Path.Combine(Path.GetTempPath(), $"algorithms-log-{Guid.NewGuid():N}.bin"),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
    private readonly List<long> offsets = [];
    private readonly object gate = new();
    private long count;

    public void Append(SortStep step)
    {
        lock (gate)
        {
            file.Position = file.Length;
            if (count % PageSize == 0)
                offsets.Add(file.Position);

            using var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
            writer.Write((byte)step.Kind);
            writer.Write(step.Message);
            count++;
        }
    }

    public (int Page, int PageCount, LogLine[] Lines) Read(int requestedPage)
    {
        lock (gate)
        {
            if (offsets.Count == 0)
                return (0, 0, []);

            var page = Math.Clamp(requestedPage, 0, offsets.Count - 1);
            var index = (long)page * PageSize;
            var length = (int)Math.Min(PageSize, count - index);
            var lines = new LogLine[length];
            file.Position = offsets[page];
            using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
            for (var i = 0; i < length; i++)
                lines[i] = new(index + i + 1, (StepKind)reader.ReadByte(), reader.ReadString());

            return (page, offsets.Count, lines);
        }
    }

    public void Dispose() => file.Dispose();
}

internal sealed record LogLine(long Index, StepKind Kind, string Message)
{
    public string Number => Index.ToString("D3");
    public string Category => Kind switch
    {
        StepKind.Start => "СТАРТ",
        StepKind.Compare => "СРАВНЕНИЕ",
        StepKind.Swap => "ОБМЕН",
        StepKind.Write => "ЗАПИСЬ",
        StepKind.Phase => "ЭТАП",
        StepKind.Complete => "ГОТОВО",
        _ => ""
    };
}
