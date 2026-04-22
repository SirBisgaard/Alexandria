using System.Threading.Channels;
using Alexandria.Api.Models.Events;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Api.Sync;

/// <summary>
/// Singleton fan-out broker for library-sync progress. The background service runs the
/// actual indexing; the broker brokers between it and any number of SSE subscribers.
/// Exclusivity is delegated to <see cref="Alexandria.Business.Interfaces.ILibraryOperationLock"/>
/// so indexing shares one busy-state with inline operations like file-management.
/// </summary>
public interface IIndexingBroker : IProgress<IndexingReport>
{
    /// <summary>True while any library operation (indexing or inline) holds the exclusivity lease.</summary>
    bool IsRunning { get; }

    /// <summary>The most recent progress frame for the in-flight run, or null if idle.</summary>
    SyncProgressEvent? LastSnapshot { get; }

    /// <summary>Enqueues a run request. Returns false if another library operation is already in flight.</summary>
    bool TryEnqueueRun();

    /// <summary>Awaits the next enqueued run request. Used by the hosted service's consume loop.</summary>
    Task WaitForRunRequest(CancellationToken cancellationToken);

    /// <summary>Called by the hosted service when it starts executing a dequeued run.</summary>
    void BeginRun();

    /// <summary>Called by the hosted service after terminal events have been broadcast, to release the run slot and close subscribers.</summary>
    void EndRun();

    /// <summary>Attaches an SSE subscriber. The returned handle closes the subscription on dispose and any cached snapshot is immediately delivered.</summary>
    IndexingSubscription Subscribe();

    /// <summary>Broadcasts a <see cref="SyncCompleteEvent"/> to all subscribers.</summary>
    void EmitComplete(int bookCount, string? lastIndexedTimestamp);

    /// <summary>Broadcasts a <see cref="SyncErrorEvent"/> to all subscribers.</summary>
    void EmitError(string message);
}

/// <summary>Disposable subscription handle owning a reader over an SSE frame channel.</summary>
public sealed class IndexingSubscription(ChannelReader<string> reader, Action onDispose) : IDisposable
{
    public ChannelReader<string> Reader => reader;
    public void Dispose() => onDispose();
}
