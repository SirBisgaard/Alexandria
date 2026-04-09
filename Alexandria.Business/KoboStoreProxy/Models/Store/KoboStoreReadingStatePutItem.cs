using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreReadingStatePutItem
{
    [JsonPropertyName("LastModified")]
    public string? LastModified { get; set; }
    [JsonPropertyName("CurrentBookmark")]
    public KoboStoreBookmark? CurrentBookmark { get; set; }
    [JsonPropertyName("Statistics")]
    public KoboStoreStatistics? Statistics { get; set; }
    [JsonPropertyName("StatusInfo")]
    public KoboStoreStatusInfo? StatusInfo { get; set; }
}