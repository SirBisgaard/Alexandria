using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.Api.Models.Responses;

public class SettingsResponse
{
    public DateTimeOffset? LastIndexedTimestamp { get; set; }
    public int SyncBatchSize { get; set; }
    public bool ProxyLoggingEnabled { get; set; }

    public int WordsPerPage { get; set; }
    public bool SyncLibraryAtBoot { get; set; }
    public string M4BFileTreatment { get; set; } = string.Empty;
    public string FileRenameTemplate { get; set; } = string.Empty;
    public string? Version { get; set; }
    public ComicRenderingSettings ComicRendering { get; set; } = new();

    /// <summary>Built-in values, for the Settings page's reset button.</summary>
    public ComicRenderingSettings ComicRenderingDefaults { get; set; } = new();
}
