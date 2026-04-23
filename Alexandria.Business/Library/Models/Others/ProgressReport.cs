namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// A single progress tick. <see cref="Task"/> describes the outer activity; <see cref="SubTask"/>
/// (when present) describes a nested operation inside the current outer tick.
/// </summary>
public sealed record ProgressReport
{
    public ProgressTask Task { get; init; } = new();
    public ProgressTask? SubTask { get; init; }
}
