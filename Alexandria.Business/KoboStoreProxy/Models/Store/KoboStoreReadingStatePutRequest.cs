using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreReadingStatePutRequest
{
    [JsonPropertyName("ReadingStates")]
    public List<KoboStoreReadingStatePutItem>? ReadingStates { get; set; }
}