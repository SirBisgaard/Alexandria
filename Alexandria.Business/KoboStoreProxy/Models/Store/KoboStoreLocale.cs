using System.Text.Json.Serialization;

namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public record KoboStoreLocale
{
    [JsonPropertyName("LanguageCode")]
    public string LanguageCode { get; set; } = "en";
    [JsonPropertyName("ScriptCode")]
    public string ScriptCode { get; set; } = "";
    [JsonPropertyName("CountryCode")]
    public string CountryCode { get; set; } = "";
}
