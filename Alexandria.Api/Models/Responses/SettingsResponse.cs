namespace Alexandria.Api.Models.Responses;

public class SettingsResponse
{
    public DateTimeOffset? LastIndexedTimestamp { get; set; }
    public int SyncBatchSize { get; set; }
}
