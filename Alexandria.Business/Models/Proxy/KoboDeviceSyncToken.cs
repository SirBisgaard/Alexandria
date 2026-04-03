namespace Alexandria.Business.Models.Proxy;

public record KoboDeviceSyncToken
{
    public int V { get; init; } = 1;
    public string KoboToken { get; init; } = "";
    public DateTimeOffset LastSync { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset ReadingStateLastModified { get; init; } = DateTimeOffset.MinValue;
}
