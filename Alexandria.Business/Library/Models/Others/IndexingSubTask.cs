namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// Nested progress for a long-running operation that happens inside a single tick of the outer
/// <see cref="IndexingReport"/> (e.g. "Transcoding" chapter 5/40 while Indexing sits on book 3/15).
/// </summary>
public struct IndexingSubTask
{
    public string Title { get; set; }
    public int Progress { get; set; }
    public int Total { get; set; }
}