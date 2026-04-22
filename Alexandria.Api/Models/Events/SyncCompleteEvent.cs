namespace Alexandria.Api.Models.Events;

public record SyncCompleteEvent
{
    public bool Done { get; init; }
    public int BookCount { get; init; }
    public string? LastIndexedTimestamp { get; init; }
}
