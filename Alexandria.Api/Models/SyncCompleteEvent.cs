namespace Alexandria.Api.Models;

public record SyncCompleteEvent(bool Done, int BookCount);
