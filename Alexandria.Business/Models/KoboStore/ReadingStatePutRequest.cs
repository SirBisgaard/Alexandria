namespace Alexandria.Business.Models.KoboStore;

public record ReadingStatePutRequest
{
    public List<ReadingStatePutItem>? ReadingStates { get; set; }
}