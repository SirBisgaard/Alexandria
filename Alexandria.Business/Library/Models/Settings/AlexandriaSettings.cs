using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.Library.Models.Settings;

/// <summary>
/// Immutable application settings bound from configuration at startup.
/// Paths are absolute; derived paths are computed from DataFolder.
/// </summary>
public record AlexandriaSettings
{
    /// <summary>Public-facing URL of the Alexandria instance (e.g. "http://books.example.com"). Used to build download URLs in Kobo sync responses.</summary>
    public required string PublicUrl { get; init; }

    /// <summary>Root directory for application data (database, covers, kepubs).</summary>
    public required string DataFolder { get; init; }

    /// <summary>Directory containing EPUB source files.</summary>
    public required string EbookFolder { get; init; }

    /// <summary>Directory containing M4B source files.</summary>
    public required string AudiobookFolder { get; init; }

    /// <summary>Full Kobo API base URL: PublicUrl + Kobo route prefix.</summary>
    public string KoboApiUrl => $"{PublicUrl}{KoboProxySettings.KoboRoutePrefix}";

    /// <summary>Absolute path to the SQLite database file.</summary>
    public string Database => Path.Combine(DataFolder, "alexandria.db");

    /// <summary>Directory for extracted/saved cover images.</summary>
    public string CoversFolder => Path.Combine(DataFolder, "covers");

    /// <summary>Directory for KEPUB derivative files.</summary>
    public string KepubsFolder => Path.Combine(DataFolder, "kepubs");

    /// <summary>Optional override for the initial M4bFileTreatment value seeded into the database.</summary>
    public M4BFileTreatment? M4BFileTreatment { get; init; }
}
