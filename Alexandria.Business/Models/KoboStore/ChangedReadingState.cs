namespace Alexandria.Business.Models.KoboStore;

public record ChangedReadingState
{
    public required ReadingState ReadingState { get; set; }
}