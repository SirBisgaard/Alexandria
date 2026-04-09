using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreDeletedTag
{
    [JsonPropertyName("Tag")]
    public required KoboStoreDeletedTagInfo Tag { get; set; }
}