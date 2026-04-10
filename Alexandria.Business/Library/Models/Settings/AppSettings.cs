namespace Alexandria.Business.Library.Models.Settings;

public class AppSettings
{
    public Guid UuidSeed { get; set; }
    public DateTimeOffset? LastIndexedTimestamp { get; set; }
    public int SyncBatchSize { get; set; } = 60;
    public bool ProxyLoggingEnabled { get; set; } = true;

    public int WordsPerPage { get; set; } = 250;
}
