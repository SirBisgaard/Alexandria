namespace Alexandria.Business.KoboStoreProxy.Models;

public record KoboDeviceSyncToken
{
    public int V { get; init; } = 1;
    public string KoboToken { get; init; } = "";
    public DateTimeOffset LastSync { get; init; } = DateTimeOffset.MinValue;
}
