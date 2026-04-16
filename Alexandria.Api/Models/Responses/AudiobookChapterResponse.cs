namespace Alexandria.Api.Models.Responses;

public class AudiobookChapterResponse
{
    public int ChapterIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public double Duration { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string? FileName { get; set; }
}
