using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreBookmarkLocation
{
    [JsonPropertyName("Value")]
    public required string Value { get; set; }
    [JsonPropertyName("Type")]
    public required string Type { get; set; }
    [JsonPropertyName("Source")]
    public required string Source { get; set; }
}