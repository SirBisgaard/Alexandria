using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreEbookProduct
{
    [JsonPropertyName("Id")]
    public required string Id { get; set; }

    [JsonPropertyName("Title")]
    public required string Title { get; set; }

    [JsonPropertyName("Contributors")]
    public string? Contributors { get; set; }

    [JsonPropertyName("ContributorRoles")]
    public List<KoboStoreContributorRole> ContributorRoles { get; set; } = [];

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("Language")]
    public string? Language { get; set; }

    [JsonPropertyName("ISBN")]
    public string? ISBN { get; set; }

    [JsonPropertyName("PublicationDate")]
    public string? PublicationDate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("IsFree")]
    public bool IsFree { get; set; }

    [JsonPropertyName("IsRecommendation")]
    public bool IsRecommendation { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("CrossRevisionId")]
    public string? CrossRevisionId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("WorkId")]
    public string? WorkId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("SeriesNumber")]
    public string? SeriesNumber { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("SeriesName")]
    public string? SeriesName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("SeriesId")]
    public string? SeriesId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("SeriesNumberFloat")]
    public float? SeriesNumberFloat { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ExternalIds")]
    public List<string>? ExternalIds { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("IsInternetArchive")]
    public bool IsInternetArchive { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Locale")]
    public KoboStoreLocale? Locale { get; set; }

    [JsonPropertyName("ImageId")]
    public string? ImageId { get; set; }

    [JsonPropertyName("PublisherName")]
    public string? PublisherName { get; set; }

    [JsonPropertyName("Rating")]
    public double Rating { get; set; }

    [JsonPropertyName("TotalRating")]
    public int TotalRating { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("RatingHistogram")]
    public Dictionary<string, int>? RatingHistogram { get; set; }

    [JsonPropertyName("Slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("IsContentSharingEnabled")]
    public bool IsContentSharingEnabled { get; set; }

    [JsonPropertyName("HasPreview")]
    public bool HasPreview { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Stats")]
    public KoboStoreEbookStats? Stats { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Price")]
    public KoboStorePrice? Price { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("PromoCodeAllowed")]
    public bool PromoCodeAllowed { get; set; }

    [JsonPropertyName("EligibleForKoboLoveDiscount")]
    public bool EligibleForKoboLoveDiscount { get; set; }

    [JsonPropertyName("IsPreOrder")]
    public bool IsPreOrder { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("InWishlist")]
    public bool InWishlist { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ApplicableSubscriptions")]
    public List<string>? ApplicableSubscriptions { get; set; }

    [JsonPropertyName("AgeVerificationRequired")]
    public bool AgeVerificationRequired { get; set; }
}

public record KoboStoreEbookStats
{
    [JsonPropertyName("Pages")]
    public int Pages { get; set; }

    [JsonPropertyName("WordCount")]
    public int WordCount { get; set; }
}
