namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// Activity-focused progress event emitted through <see cref="Alexandria.Business.Library.Interfaces.IIndexingProgressService"/>.
/// <see cref="Title"/> describes the current activity (e.g. "Converting..."),
/// <see cref="Subtitle"/> names the current unit of work (typically the book title),
/// and <see cref="Progress"/>/<see cref="Total"/> express overall completion within the activity.
/// <see cref="SubTask"/>, when present, describes a nested long-running operation inside the
/// current tick (e.g. chapter transcoding while the outer Indexing tick advances once per book).
/// </summary>
public struct IndexingReport
{
    public string Title { get; set; }
    public string? Subtitle { get; set; }
    public int Progress { get; set; }
    public int Total { get; set; }
    public IndexingSubTask? SubTask { get; set; }
}