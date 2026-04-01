using System.Text.Json.Serialization;

namespace Alexandria.Business.Models.KoboStore;

public record Statistics
{
    public required string LastModified { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SpentReadingMinutes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RemainingTimeMinutes { get; set; }
}