using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreExternalId
{
    [JsonPropertyName("Id")]
    public required string Id { get; set; }
}
