namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// Terminal payload from the sync/indexing task. Carries the book count for logging /
/// telemetry and the last-indexed timestamp so the frontend can update its "LAST SYNC" line.
/// </summary>
public sealed record SyncCompletion
{
    public int BookCount { get; init; }
    public string? LastIndexedTimestamp { get; init; }
}
