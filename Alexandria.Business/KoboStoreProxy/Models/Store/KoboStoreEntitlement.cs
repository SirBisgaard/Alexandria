using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreEntitlement
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("BookEntitlement")]
    public KoboStoreBookEntitlement? BookEntitlement { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("BookMetadata")]
    public KoboStoreBookMetadata? BookMetadata { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("AudiobookEntitlement")]
    public KoboStoreBookEntitlement? AudiobookEntitlement { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("AudiobookMetadata")]
    public KoboStoreBookMetadata? AudiobookMetadata { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ReadingState")]
    public KoboStoreReadingState? ReadingState { get; set; }
}