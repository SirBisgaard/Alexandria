using System.Threading.Channels;
using Alexandria.Api.Models.Events;
using Alexandria.Business.Library.Interfaces;

namespace Alexandria.Api.Sync;

/// <summary>
/// Singleton fan-out broker for background library tasks. Hosts a 1-deep queue of
/// <see cref="IBackgroundTask"/> types, the exclusivity lease, N SSE subscribers, and the
/// last-seen progress snapshot for late attach. The broker itself is the
/// <see cref="IProgress{T}"/> sink the scoped <see cref="IProgressService"/> reports to.
/// Exclusivity is delegated to <see cref="Business.Interfaces.ILibraryOperationLock"/>
/// so every library operation shares one busy-state.
/// </summary>
public interface ILibraryTaskBroker : IProgress<object>
{
    /// <summary>True while any library task holds the exclusivity lease.</summary>
    bool IsRunning { get; }

    /// <summary>Kind of the currently running task (e.g. "sync", "file-management"), or null if idle.</summary>
    string? RunningKind { get; }

    /// <summary>The most recent progress frame for the in-flight run, or null if idle.</summary>
    ProgressEvent? LastSnapshot { get; }

    /// <summary>Enqueues a task by type. Returns false if another library operation is already in flight.</summary>
    bool TryEnqueue(Type taskType);

    /// <summary>Awaits the next enqueued task type. Used by the hosted service's consume loop.</summary>
    Task<Type> WaitForTask(CancellationToken cancellationToken);

    /// <summary>Called by the hosted service when it starts executing a dequeued task.</summary>
    void BeginRun(IBackgroundTask task);

    /// <summary>Called by the hosted service after terminal events have been broadcast, to release the run slot and close subscribers.</summary>
    void EndRun();

    /// <summary>Attaches an SSE subscriber. Any cached snapshot is delivered immediately.</summary>
    LibraryTaskSubscription Subscribe();

    /// <summary>Maps <paramref name="exception"/> to a terminal SSE frame via the current task's formatter and broadcasts.</summary>
    void EmitError(Exception exception);
}

/// <summary>Disposable subscription handle owning a reader over an SSE frame channel.</summary>
public sealed class LibraryTaskSubscription(ChannelReader<string> reader, Action onDispose) : IDisposable
{
    public ChannelReader<string> Reader => reader;
    public void Dispose() => onDispose();
}
