using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreLoveDisplayPrice
{
    [JsonPropertyName("TotalAmount")]
    public int TotalAmount { get; set; } = 0;
}