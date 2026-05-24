namespace Alexandria.Business.KoboStoreProxy.Models;

public sealed record BookActivityEvent
{
    public required string EventId { get; init; }
    public required Guid BookUuid { get; init; }
    public required string DeviceUuid { get; init; }
    public required string EventType { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
}
