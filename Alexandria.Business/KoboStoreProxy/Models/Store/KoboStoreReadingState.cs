using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreReadingState
{
    [JsonPropertyName("EntitlementId")]
    public required string EntitlementId { get; set; }
    [JsonPropertyName("Created")]
    public required string Created { get; set; }
    [JsonPropertyName("LastModified")]
    public required string LastModified { get; set; }
    [JsonPropertyName("PriorityTimestamp")]
    public required string PriorityTimestamp { get; set; }
    [JsonPropertyName("StatusInfo")]
    public required KoboStoreStatusInfo StatusInfo { get; set; }
    [JsonPropertyName("Statistics")]
    public required KoboStoreStatistics Statistics { get; set; }
    [JsonPropertyName("CurrentBookmark")]
    public required KoboStoreBookmark CurrentBookmark { get; set; }
}