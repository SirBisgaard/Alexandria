namespace Alexandria.Api.Models.Events;

public record SyncCompleteEvent(bool Done, int BookCount, string? LastIndexedTimestamp);
