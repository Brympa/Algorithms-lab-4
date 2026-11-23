namespace AlgorithmsLab.Domain;

public enum StepKind { Start, Compare, Swap, Write, Phase, Complete }

public sealed record SortStep(
    StepKind Kind, string Message, int First = -1, int Second = -1,
    int RangeStart = -1, int RangeEnd = -1, string? Label = null);

public delegate void SortObserver<T>(SortStep step, T[] values);
