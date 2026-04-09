using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreTagItem
{
    [JsonPropertyName("RevisionId")]
    public required string RevisionId { get; set; }
    [JsonPropertyName("Type")]
    public string Type { get; set; } = "ProductRevisionTagItem";
}