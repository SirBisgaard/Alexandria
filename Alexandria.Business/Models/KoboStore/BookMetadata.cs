using System.Text.Json.Serialization;

namespace Alexandria.Business.Models.KoboStore;

public record BookMetadata
{
    public string[] Categories { get; set; } = ["00000000-0000-0000-0000-000000000001"];
    public required string CoverImageId { get; set; }
    public required string CrossRevisionId { get; set; }
    public DisplayPrice CurrentDisplayPrice { get; set; } = new();
    public LoveDisplayPrice CurrentLoveDisplayPrice { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    public required List<DownloadUrl> DownloadUrls { get; set; }
    public required string EntitlementId { get; set; }
    public ExternalId[] ExternalIds { get; set; } = [];
    public string Genre { get; set; } = "00000000-0000-0000-0000-000000000001";
    public bool IsEligibleForKoboLove { get; set; }
    public bool IsInternetArchive { get; set; }
    public bool IsPreOrder { get; set; }
    public bool IsSocialEnabled { get; set; } = true;
    public required string Language { get; set; }
    public object PhoneticPronunciations { get; set; } = new { };
    public required string PublicationDate { get; set; }
    public required Publisher Publisher { get; set; }
    public required string RevisionId { get; set; }
    public required string Title { get; set; }
    public required string WorkId { get; set; }

    public List<ContributorRole>? ContributorRoles { get; set; }
    public List<string>? Contributors { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SeriesInfo? Series { get; set; }
}