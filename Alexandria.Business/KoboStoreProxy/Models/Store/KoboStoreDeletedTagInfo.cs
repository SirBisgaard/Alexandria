using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreDeletedTagInfo
{
    [JsonPropertyName("Id")]
    public required string Id { get; set; }
    [JsonPropertyName("LastModified")]
    public required string LastModified { get; set; }
}