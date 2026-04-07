namespace Alexandria.Business.Models;

public class ReadingProgress
{
    public ReadingStatus Status { get; set; } = ReadingStatus.ReadyToRead;
    public decimal? ProgressPercent { get; set; }
    public int? SpentReadingMinutes { get; set; }
    public int? RemainingTimeMinutes { get; set; }
    public DateTimeOffset LastModified { get; set; }
}
