using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreBookEntitlement
{
    [JsonPropertyName("Accessibility")]
    public string Accessibility { get; set; } = "Full";
    [JsonPropertyName("ActivePeriod")]
    public required KoboStoreActivePeriod ActivePeriod { get; set; }
    [JsonPropertyName("Created")]
    public required string Created { get; set; }
    [JsonPropertyName("CrossRevisionId")]
    public required string CrossRevisionId { get; set; }
    [JsonPropertyName("Id")]
    public required string Id { get; set; }
    [JsonPropertyName("IsRemoved")]
    public bool IsRemoved { get; set; }
    [JsonPropertyName("IsHiddenFromArchive")]
    public bool IsHiddenFromArchive { get; set; }
    [JsonPropertyName("IsLocked")]
    public bool IsLocked { get; set; }
    [JsonPropertyName("LastModified")]
    public required string LastModified { get; set; }
    [JsonPropertyName("OriginCategory")]
    public string OriginCategory { get; set; } = "Imported";
    [JsonPropertyName("RevisionId")]
    public required string RevisionId { get; set; }
    [JsonPropertyName("Status")]
    public string Status { get; set; } = "Active";
}