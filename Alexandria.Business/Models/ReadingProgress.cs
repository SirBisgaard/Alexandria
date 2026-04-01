namespace Alexandria.Business.Models;

public class ReadingProgress
{
    public string Status { get; set; } = "ReadyToRead";
    public double? ProgressPercent { get; set; }
    public int? SpentReadingMinutes { get; set; }
    public int? RemainingTimeMinutes { get; set; }
    public DateTime LastModified { get; set; }
}
