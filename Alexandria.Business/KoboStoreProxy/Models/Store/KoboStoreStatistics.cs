using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreStatistics
{
    [JsonPropertyName("LastModified")]
    public required string LastModified { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("SpentReadingMinutes")]
    public int? SpentReadingMinutes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("RemainingTimeMinutes")]
    public int? RemainingTimeMinutes { get; set; }
}