using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

// TODO: split KoboStoreProductPriceItem into its own file to match the one-record-per-file standard.
public record KoboStoreProductPrices
{
    [JsonPropertyName("Items")]
    public List<KoboStoreProductPriceItem> Items { get; set; } = [];
}

public record KoboStoreProductPriceItem
{
    [JsonPropertyName("Id")]
    public required string Id { get; set; }

    [JsonPropertyName("Price")]
    public required KoboStorePrice Price { get; set; }

    [JsonPropertyName("EligibleForKoboLoveDiscount")]
    public bool EligibleForKoboLoveDiscount { get; set; }

    [JsonPropertyName("IsPreOrder")]
    public bool IsPreOrder { get; set; }
}
