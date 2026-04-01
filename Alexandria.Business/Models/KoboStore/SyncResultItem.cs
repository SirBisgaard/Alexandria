using System.Text.Json.Serialization;

namespace Alexandria.Business.Models.KoboStore;

public record SyncResultItem
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Entitlement? NewEntitlement { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Entitlement? ChangedEntitlement { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChangedReadingState? ChangedReadingState { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TagContainer? NewTag { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TagContainer? ChangedTag { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeletedTag? DeletedTag { get; set; }
}