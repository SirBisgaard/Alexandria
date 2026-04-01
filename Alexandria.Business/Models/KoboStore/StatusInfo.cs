using System.Text.Json.Serialization;

namespace Alexandria.Business.Models.KoboStore;

public record StatusInfo
{
    public required string LastModified { get; set; }
    public required string Status { get; set; }  // "ReadyToRead", "Reading", "Finished"
    public int TimesStartedReading { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastTimeStartedReading { get; set; }
}