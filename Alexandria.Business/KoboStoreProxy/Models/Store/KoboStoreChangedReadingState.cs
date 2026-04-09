using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

/// <summary>
/// A sync result item that pushes updated reading state to a Kobo device.
/// Kobo devices ignore KoboStoreReadingState embedded in ChangedEntitlement for books they already have locally.
/// A separate KoboStoreChangedReadingState sync item is the only way to update reading state on existing books.
/// </summary>
public record KoboStoreChangedReadingState
{
    [JsonPropertyName("ReadingState")]
    public required KoboStoreReadingState ReadingState { get; set; }
}