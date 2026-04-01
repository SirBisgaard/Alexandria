using System.Text.Json.Serialization;

namespace Alexandria.Business.Models.KoboStore;

public record Bookmark
{
    public required string LastModified { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ProgressPercent { get; set; }  // int when whole, float otherwise

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ContentSourceProgressPercent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BookmarkLocation? Location { get; set; }
}