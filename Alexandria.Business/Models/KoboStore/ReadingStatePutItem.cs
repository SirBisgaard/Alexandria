namespace Alexandria.Business.Models.KoboStore;

public record ReadingStatePutItem
{
    public Bookmark? CurrentBookmark { get; set; }
    public Statistics? Statistics { get; set; }
    public StatusInfo? StatusInfo { get; set; }
}