namespace Alexandria.Business.Models.KoboStore;

public record BookmarkLocation
{
    public required string Value { get; set; }
    public required string Type { get; set; }
    public required string Source { get; set; }
}