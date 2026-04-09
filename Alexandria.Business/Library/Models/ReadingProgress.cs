namespace Alexandria.Business.Library.Models;

public class ReadingProgress
{
    public ReadingStatus Status { get; set; } = ReadingStatus.ReadyToRead;
    public decimal? ProgressPercent { get; set; }
    public int? SpentReadingMinutes { get; set; }
    public int? RemainingTimeMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
