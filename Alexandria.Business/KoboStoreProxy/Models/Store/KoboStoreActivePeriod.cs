using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreActivePeriod
{
    [JsonPropertyName("From")]
    public required string From { get; set; }
}