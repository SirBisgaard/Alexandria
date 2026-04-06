namespace Alexandria.Business.Models.Settings;

public class AppSettings
{
    public Guid UuidSeed { get; set; }
    public DateTimeOffset? LastIndexedTimestamp { get; set; }
    public int SyncBatchSize { get; set; } = 60;
}
