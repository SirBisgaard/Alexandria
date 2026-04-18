using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Services;

/// <summary>
/// Default <see cref="IIndexingProgressService"/>. Designed for scoped DI lifetime:
/// the controller that initiates a sync subscribes once, then every indexer and
/// converter resolved within the same scope publishes through the same instance.
/// Transient lifetime would give each consumer its own isolated sink and break
/// the plumbing.
/// Outer reports (<see cref="ReportConverting"/>, <see cref="ReportIndexing"/>)
/// are expected to be issued serially from the indexing pipeline. Sub-task reports
/// may come from tight inner loops; because each outer tick is published and cached
/// before any sub-task begins, the cached outer snapshot is stable while sub-tasks
/// stream against it.
/// </summary>
public class IndexingProgressService : IIndexingProgressService
{
    private IProgress<IndexingReport>? indexingProgress;
    private IndexingReport lastOuter;
    private bool hasOuter;

    public void Subscribe(IProgress<IndexingReport> sink)
    {
        indexingProgress = sink;
    }

    public void ReportConverting(string bookTitle, int index, int total)
    {
        Emit(new IndexingReport
        {
            Title = "Converting",
            Subtitle = bookTitle,
            Progress = index,
            Total = total
        });
    }

    public void ReportIndexing(string bookTitle, int index, int total)
    {
        Emit(new IndexingReport
        {
            Title = "Indexing",
            Subtitle = bookTitle,
            Progress = index,
            Total = total
        });
    }

    public void ReportSubTask(string title, int progress, int total)
    {
        if (!hasOuter)
            return;

        var report = lastOuter;
        report.SubTask = new IndexingSubTask
        {
            Title = title,
            Progress = progress,
            Total = total
        };
        this.indexingProgress?.Report(report);
    }

    public void ReportDone()
    {
        Emit(new IndexingReport
        {
            Title = "Done",
            Progress = 1,
            Total = 1
        });
    }

    /// <summary>
    /// Caches the new outer state and emits it. The <see cref="IndexingReport"/> struct is
    /// freshly constructed at each call site, so its <see cref="IndexingReport.SubTask"/>
    /// is always <c>null</c> here — that's what clears the previous sub-task for free.
    /// </summary>
    private void Emit(IndexingReport report)
    {
        lastOuter = report;
        hasOuter = true;
        indexingProgress?.Report(report);
    }
}
