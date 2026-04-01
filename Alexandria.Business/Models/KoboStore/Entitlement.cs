using System.Text.Json.Serialization;

namespace Alexandria.Business.Models.KoboStore;

public record Entitlement
{
    public required BookEntitlement BookEntitlement { get; set; }
    public required BookMetadata BookMetadata { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReadingState? ReadingState { get; set; }
}