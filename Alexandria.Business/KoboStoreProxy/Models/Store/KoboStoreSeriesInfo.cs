using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreSeriesInfo
{
    [JsonPropertyName("Name")]
    public required string Name { get; set; }
    [JsonPropertyName("Number")]
    public required string Number { get; set; }
    [JsonPropertyName("NumberFloat")]
    public required float NumberFloat { get; set; }
    [JsonPropertyName("Id")]
    public required string Id { get; set; }
}