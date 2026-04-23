namespace Alexandria.Business.Library.Interfaces;

/// <summary>
/// A long-running library operation run by the background-task infrastructure. One
/// implementation per flow (sync, file-management, …); each task owns its own SSE
/// event formatting so the shared broker can stay flow-agnostic.
/// </summary>
public interface IBackgroundTask
{
    /// <summary>Stable identifier for the task kind, surfaced in the status endpoint (e.g. "sync").</summary>
    string Kind { get; }

    /// <summary>Runs the task. Progress ticks and the terminal payload flow through <paramref name="progress"/>.</summary>
    Task Execute(IProgressService progress, CancellationToken cancellationToken);

    /// <summary>Maps a <c>ReportDone&lt;T&gt;</c> payload to an SSE frame body (no <c>data: </c> prefix).</summary>
    string FormatTerminal(object doneObject);

    /// <summary>Maps an exception escaping <see cref="Execute"/> to an SSE frame body (no <c>data: </c> prefix).</summary>
    string FormatError(Exception exception);
}
