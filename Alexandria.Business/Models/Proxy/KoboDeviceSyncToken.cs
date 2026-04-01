namespace Alexandria.Business.Models.Proxy;

public record KoboDeviceSyncToken
{
    public int V { get; init; } = 1;
    public string KoboToken { get; init; } = "";
    public DateTime LastSync { get; init; } = DateTime.MinValue;
}
