using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.Library.Models.Settings;

public record AlexandriaSettings
{
    public required string PublicUrl { get; init; }
    public required string DataFolder { get; init; }
    public required string EbookFolder { get; init; }
    public required string AudiobookFolder { get; init; }

    public string KoboApiUrl => $"{PublicUrl}{KoboProxySettings.KoboRoutePrefix}";
    public string Database => Path.Combine(DataFolder, "alexandria.db");
    public string CoversFolder => Path.Combine(DataFolder, "covers");
    public string KepubsFolder => Path.Combine(DataFolder, "kepubs");
}