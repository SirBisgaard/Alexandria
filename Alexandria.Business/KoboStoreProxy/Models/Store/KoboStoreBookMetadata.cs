using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreBookMetadata
{
    [JsonPropertyName("Categories")]
    public string[] Categories { get; set; } = ["00000000-0000-0000-0000-000000000001"];
    [JsonPropertyName("CoverImageId")]
    public required string CoverImageId { get; set; }
    [JsonPropertyName("CrossRevisionId")]
    public required string CrossRevisionId { get; set; }
    [JsonPropertyName("CurrentDisplayPrice")]
    public KoboStoreDisplayPrice CurrentDisplayPrice { get; set; } = new();
    [JsonPropertyName("CurrentLoveDisplayPrice")]
    public KoboStoreLoveDisplayPrice CurrentLoveDisplayPrice { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("DownloadUrls")]
    public required List<KoboStoreDownloadUrl> DownloadUrls { get; set; }
    [JsonPropertyName("EntitlementId")]
    public required string EntitlementId { get; set; }
    [JsonPropertyName("ExternalIds")]
    public KoboStoreExternalId[] ExternalIds { get; set; } = [];
    [JsonPropertyName("Genre")]
    public string Genre { get; set; } = "00000000-0000-0000-0000-000000000001";
    [JsonPropertyName("IsEligibleForKoboLove")]
    public bool IsEligibleForKoboLove { get; set; }
    [JsonPropertyName("IsInternetArchive")]
    public bool IsInternetArchive { get; set; }
    [JsonPropertyName("IsPreOrder")]
    public bool IsPreOrder { get; set; }
    [JsonPropertyName("IsSocialEnabled")]
    public bool IsSocialEnabled { get; set; } = true;
    [JsonPropertyName("Language")]
    public required string Language { get; set; }
    [JsonPropertyName("PhoneticPronunciations")]
    public object PhoneticPronunciations { get; set; } = new { };
    [JsonPropertyName("PublicationDate")]
    public required string PublicationDate { get; set; }
    [JsonPropertyName("Publisher")]
    public required KoboStorePublisher Publisher { get; set; }
    [JsonPropertyName("RevisionId")]
    public required string RevisionId { get; set; }
    [JsonPropertyName("Title")]
    public required string Title { get; set; }
    [JsonPropertyName("WorkId")]
    public required string WorkId { get; set; }

    [JsonPropertyName("ContributorRoles")]
    public List<KoboStoreContributorRole>? ContributorRoles { get; set; }
    [JsonPropertyName("Contributors")]
    public List<string>? Contributors { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Series")]
    public KoboStoreSeriesInfo? Series { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Duration")]
    public int? Duration { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("IsAbridged")]
    public bool? IsAbridged { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Locale")]
    public KoboStoreLocale? Locale { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("RelatedGroupId")]
    public string? RelatedGroupId { get; set; }
}