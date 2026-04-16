namespace Alexandria.Api.Models.Responses;

public class SettingsResponse
{
    public DateTimeOffset? LastIndexedTimestamp { get; set; }
    public int SyncBatchSize { get; set; }
    public bool ProxyLoggingEnabled { get; set; }

    public int WordsPerPage { get; set; }
    public bool SyncLibraryAtBoot { get; set; }
    public string M4BFileTreatment { get; set; } = string.Empty;
    public string? Version { get; set; }
}
