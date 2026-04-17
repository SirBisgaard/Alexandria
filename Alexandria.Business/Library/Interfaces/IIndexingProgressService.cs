using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

/// <summary>
/// Scoped per-sync progress broker. Consumers (e.g. the SSE sync endpoint) call
/// <see cref="Subscribe"/> once at the start of a sync operation; publishers
/// (the indexer, converters, derivative builders) call the <c>Report*</c>
/// methods without needing to thread an <see cref="IProgress{T}"/> through their
/// signatures. The service remembers the last outer activity so that
/// <see cref="ReportSubTask"/> can augment it without the publisher needing to
/// re-supply the outer title/counters. Every outer report (<see cref="ReportConverting"/>,
/// <see cref="ReportIndexing"/>) implicitly clears any active sub-task, so publishers
/// never need to clear explicitly.
/// </summary>
public interface IIndexingProgressService
{
    /// <summary>Registers the sink that receives every <see cref="IndexingReport"/> for the current scope.</summary>
    void Subscribe(IProgress<IndexingReport> sink);

    /// <summary>Reports progress of the pre-index M4B → MP3 folder conversion, one tick per book. Any previously reported sub-task is implicitly cleared.</summary>
    void ReportConverting(string bookTitle, int index, int total);

    /// <summary>Reports progress of the per-file indexing loop, one tick per file. Any previously reported sub-task is implicitly cleared.</summary>
    void ReportIndexing(string bookTitle, int index, int total);

    /// <summary>
    /// Reports a nested long-running operation inside the current outer tick (e.g. chapter
    /// transcoding while Indexing sits on one book). Emits a full report carrying the last
    /// outer state plus the new sub-task. No-op if no outer activity has been reported yet.
    /// </summary>
    void ReportSubTask(string title, int progress, int total);

    /// <summary>
    /// Signals the end of the sync: emits a final report with <c>Title = "Done"</c> and a
    /// full progress bar (1/1). The subsequent <c>SyncCompleteEvent</c> carries the book
    /// count and last-indexed timestamp; this event just tells the frontend the progress
    /// stream has no more ticks coming.
    /// </summary>
    void ReportDone();
}
