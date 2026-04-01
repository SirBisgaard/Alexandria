using System.Text.Json.Serialization;

namespace Alexandria.Business.Models.KoboStore;

public record Publisher
{
    public string Imprint { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
}