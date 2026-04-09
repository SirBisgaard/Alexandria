using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreTag
{
    [JsonPropertyName("Created")]
    public required string Created { get; set; }
    [JsonPropertyName("Id")]
    public required string Id { get; set; }
    [JsonPropertyName("Items")]
    public List<KoboStoreTagItem>? Items { get; set; }
    [JsonPropertyName("LastModified")]
    public required string LastModified { get; set; }
    [JsonPropertyName("Name")]
    public required string Name { get; set; }
    [JsonPropertyName("Type")]
    public string Type { get; set; } = "UserTag";
}