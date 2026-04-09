using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreProductRecommendations
{
    [JsonPropertyName("Items")]
    public List<KoboStoreProductRecommendationItem> Items { get; set; } = [];

    [JsonPropertyName("ItemCount")]
    public int ItemCount { get; set; }

    [JsonPropertyName("TotalPageCount")]
    public int TotalPageCount { get; set; }

    [JsonPropertyName("TotalItemCount")]
    public int TotalItemCount { get; set; }

    [JsonPropertyName("CurrentPageIndex")]
    public int CurrentPageIndex { get; set; }

    [JsonPropertyName("ItemsPerPage")]
    public int ItemsPerPage { get; set; } = 100;

    [JsonPropertyName("VersionCode")]
    public int VersionCode { get; set; } = 2;
}

public record KoboStoreProductRecommendationItem
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Audiobook")]
    public KoboStoreProductRecommendationAudiobook? Audiobook { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Book")]
    public KoboStoreEbookProduct? Book { get; set; }
}

public record KoboStoreProductRecommendationAudiobook
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

    [JsonPropertyName("Duration")]
    public int Duration { get; set; }

    [JsonPropertyName("TotalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("IsAbridged")]
    public bool IsAbridged { get; set; }

    [JsonPropertyName("HasPreview")]
    public bool HasPreview { get; set; }

    [JsonPropertyName("IsFree")]
    public bool IsFree { get; set; }

    [JsonPropertyName("ISBN")]
    public string? ISBN { get; set; }

    [JsonPropertyName("PublicationDate")]
    public string? PublicationDate { get; set; }

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
    [JsonPropertyName("Subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("IsRecommendation")]
    public bool IsRecommendation { get; set; }

    [JsonPropertyName("CrossRevisionId")]
    public string? CrossRevisionId { get; set; }

    [JsonPropertyName("ImageId")]
    public string? ImageId { get; set; }

    [JsonPropertyName("PublisherName")]
    public string? PublisherName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Edition")]
    public string? Edition { get; set; }

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Price")]
    public KoboStorePrice? Price { get; set; }

    [JsonPropertyName("EligibleForKoboLoveDiscount")]
    public bool EligibleForKoboLoveDiscount { get; set; }

    [JsonPropertyName("IsPreOrder")]
    public bool IsPreOrder { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ApplicableSubscriptions")]
    public List<string>? ApplicableSubscriptions { get; set; }

    [JsonPropertyName("AgeVerificationRequired")]
    public bool AgeVerificationRequired { get; set; }
}

public record KoboStorePrice
{
    [JsonPropertyName("Currency")]
    public required string Currency { get; set; }

    [JsonPropertyName("Price")]
    public decimal Price { get; set; }
}
