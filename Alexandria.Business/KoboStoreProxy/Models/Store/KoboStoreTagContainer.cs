using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreTagContainer
{
    [JsonPropertyName("Tag")]
    public required KoboStoreTag Tag { get; set; }
}