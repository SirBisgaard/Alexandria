namespace Alexandria.Business.Library.Models.Settings;

/// <summary>
/// Mutable application settings stored in the Settings database table.
/// </summary>
public class AppSettings
{
    /// <summary>UUID v5 namespace seed used for deterministic book/series UUID generation. Stored in DB, loaded at boot.</summary>
    public Guid UuidSeed { get; set; }

    /// <summary>Timestamp of the last completed library index. Null if never indexed.</summary>
    public DateTimeOffset? LastIndexedTimestamp { get; set; }

    /// <summary>Number of local books injected per Kobo sync response. Controls pagination during phase 2 sync.</summary>
    public int SyncBatchSize { get; set; } = 60;

    /// <summary>When true, all Kobo proxy traffic is logged to the KoboTrafficLogs table.</summary>
    public bool ProxyLoggingEnabled { get; set; } = true;

    /// <summary>Words per page for PageCount estimation from WordCount.</summary>
    public int WordsPerPage { get; set; } = 250;

    /// <summary>When true, the library is re-indexed on application startup.</summary>
    public bool SyncLibraryAtBoot { get; set; }

    /// <summary>Controls how M4B audiobook files are processed during library indexing.</summary>
    public M4bFileTreatment M4bFileTreatment { get; set; } = M4bFileTreatment.Nothing;
}
