namespace Alexandria.Business.Models.KoboStore;

public record ReadingState
{
    public required string EntitlementId { get; set; }
    public required string Created { get; set; }
    public required string LastModified { get; set; }
    public required string PriorityTimestamp { get; set; }
    public required StatusInfo StatusInfo { get; set; }
    public required Statistics Statistics { get; set; }
    public required Bookmark CurrentBookmark { get; set; }
}