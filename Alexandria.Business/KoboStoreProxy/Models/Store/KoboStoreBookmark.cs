using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreBookmark
{
    [JsonPropertyName("LastModified")]
    public required string LastModified { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ProgressPercent")]
    public object? ProgressPercent { get; set; }  // int when whole, float otherwise

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ContentSourceProgressPercent")]
    public object? ContentSourceProgressPercent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Location")]
    public KoboStoreBookmarkLocation? Location { get; set; }
}