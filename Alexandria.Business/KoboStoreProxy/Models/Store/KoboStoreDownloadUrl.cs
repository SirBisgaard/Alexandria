using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreDownloadUrl
{
    [JsonPropertyName("DrmType")]
    public string DrmType { get; set; } = "None";
    [JsonPropertyName("Format")]
    public required string Format { get; set; }  // "KEPUB", "EPUB", "EPUB3", "EPUB3FL", "MANIFEST"
    [JsonPropertyName("Size")]
    public required long Size { get; set; }
    [JsonPropertyName("Url")]
    public required string Url { get; set; }
    [JsonPropertyName("Platform")]
    public string Platform { get; set; } = "Generic";
}