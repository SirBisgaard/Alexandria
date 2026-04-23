namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// One unit of work within a longer-running operation. <see cref="Title"/> is the activity
/// label (e.g. "Indexing"), <see cref="Count"/>/<see cref="Total"/> express progress.
/// </summary>
public sealed record ProgressTask
{
    public string Title { get; init; } = string.Empty;
    public int Count { get; init; }
    public int Total { get; init; }
}
