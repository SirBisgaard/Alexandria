using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreStatusInfo
{
    [JsonPropertyName("LastModified")]
    public required string LastModified { get; set; }
    [JsonPropertyName("Status")]
    public required string Status { get; set; }  // "ReadyToRead", "Reading", "Finished"
    [JsonPropertyName("TimesStartedReading")]
    public int TimesStartedReading { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("LastTimeStartedReading")]
    public string? LastTimeStartedReading { get; set; }
}