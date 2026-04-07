using Alexandria.Business.Models;

namespace Alexandria.Api.Models.Responses;

public class ReadingProgressResponse
{
    public ReadingStatus Status { get; set; } = ReadingStatus.ReadyToRead;
    public double? ProgressPercent { get; set; }
    public int? SpentReadingMinutes { get; set; }
    public int? RemainingTimeMinutes { get; set; }
    public DateTimeOffset LastModified { get; set; }
}
