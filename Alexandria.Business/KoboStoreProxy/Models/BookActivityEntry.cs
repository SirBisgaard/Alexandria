namespace Alexandria.Business.KoboStoreProxy.Models;

public sealed record BookActivityEntry
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required DateTimeOffset EventTimestamp { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required string Description { get; init; }
}
