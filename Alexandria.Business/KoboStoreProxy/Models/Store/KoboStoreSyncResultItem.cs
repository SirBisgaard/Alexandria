using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreSyncResultItem
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("NewEntitlement")]
    public KoboStoreEntitlement? NewEntitlement { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ChangedEntitlement")]
    public KoboStoreEntitlement? ChangedEntitlement { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ChangedReadingState")]
    public KoboStoreChangedReadingState? ChangedReadingState { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("NewTag")]
    public KoboStoreTagContainer? NewTag { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ChangedTag")]
    public KoboStoreTagContainer? ChangedTag { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("DeletedTag")]
    public KoboStoreDeletedTag? DeletedTag { get; set; }
}