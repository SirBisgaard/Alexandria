using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

// TODO: split KoboStoreAnalyticsEvent into its own file to match the one-record-per-file standard.
public record KoboStoreAnalyticsRequest
{
    [JsonPropertyName("AffiliateName")]
    public string? AffiliateName { get; set; }

    [JsonPropertyName("ApplicationVersion")]
    public string? ApplicationVersion { get; set; }

    [JsonPropertyName("Events")]
    public List<KoboStoreAnalyticsEvent>? Events { get; set; }

    [JsonPropertyName("PlatformId")]
    public string? PlatformId { get; set; }

    [JsonPropertyName("SerialNumber")]
    public string? SerialNumber { get; set; }
}

public record KoboStoreAnalyticsEvent
{
    [JsonPropertyName("Attributes")]
    public Dictionary<string, string>? Attributes { get; set; }

    [JsonPropertyName("ClientApplicationVersion")]
    public string? ClientApplicationVersion { get; set; }

    [JsonPropertyName("EventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("Metrics")]
    public Dictionary<string, object>? Metrics { get; set; }

    [JsonPropertyName("TestGroups")]
    public Dictionary<string, object>? TestGroups { get; set; }

    [JsonPropertyName("Timestamp")]
    public string? Timestamp { get; set; }
}
