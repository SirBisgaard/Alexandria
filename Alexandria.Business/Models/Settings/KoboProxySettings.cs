namespace Alexandria.Business.Models.Settings;

public record KoboProxySettings
{
    public required string AlexandriaApi { get; init; }
    public required string StoreApi { get; init; }
    public string KoboRoutePrefix { get; init; } = "/api/kobo";
    public int SyncBatchSize { get; init; } = 100;

    public string KoboApiUrl => $"{AlexandriaApi}{KoboRoutePrefix}";
}