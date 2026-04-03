namespace Alexandria.Business.Models.KoboStore;

public record ReadingStatePutItem
{
    public string? LastModified { get; set; }
    public Bookmark? CurrentBookmark { get; set; }
    public Statistics? Statistics { get; set; }
    public StatusInfo? StatusInfo { get; set; }
}