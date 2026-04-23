using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

/// <summary>
/// Scoped progress broker for a single long-running operation. The consumer (SSE endpoint,
/// hosted service, etc.) calls <see cref="Subscribe"/> once with an <see cref="IProgress{T}"/>
/// over <c>object</c>; publishers call <see cref="Report"/>, <see cref="ReportSubTask"/>, and
/// <see cref="ReportDone{T}"/> without threading sinks through their signatures. Progress ticks
/// arrive as <see cref="ProgressReport"/>; terminal payloads arrive as whatever type the caller
/// passed to <see cref="ReportDone{T}"/>. The subscriber pattern-matches and serializes per
/// flow. Each outer <see cref="Report"/> implicitly clears any active sub-task.
/// </summary>
public interface IProgressService
{
    /// <summary>Registers the sink that receives every event emitted in this scope.</summary>
    void Subscribe(IProgress<object> sink);

    /// <summary>Emits an outer tick as a <see cref="ProgressReport"/>. Clears any previously reported sub-task.</summary>
    void Report(string title, int count, int total);

    /// <summary>
    /// Emits a nested tick inside the current outer tick as a <see cref="ProgressReport"/>
    /// carrying the cached outer state plus the new sub-task. No-op if no outer activity has
    /// been reported yet.
    /// </summary>
    void ReportSubTask(string title, int count, int total);

    /// <summary>
    /// Emits the terminal payload for the operation. Shape is flow-specific; the subscriber
    /// is responsible for interpreting it (typically by pattern match on type).
    /// </summary>
    void ReportDone<T>(T doneObject);
}
