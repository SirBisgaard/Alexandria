namespace Alexandria.Business.KoboStoreProxy.Models;

public record KoboProxySettings
{
    public required string AlexandriaApi { get; init; }
    public required string StoreApi { get; init; }
    public string KoboRoutePrefix { get; init; } = "/api/kobo";

    public string KoboApiUrl => $"{AlexandriaApi}{KoboRoutePrefix}";
}