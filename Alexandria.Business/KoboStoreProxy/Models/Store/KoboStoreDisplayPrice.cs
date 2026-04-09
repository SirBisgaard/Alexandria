using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreDisplayPrice
{
    [JsonPropertyName("CurrencyCode")]
    public string CurrencyCode { get; set; } = "USD";
    [JsonPropertyName("TotalAmount")]
    public int TotalAmount { get; set; } = 0;
}