namespace Alexandria.DataAccess.Externals.Models;

public record FfmpegChapter
{
    public long StartMs { get; init; }
    public long EndMs { get; init; }
    public string Title { get; init; } = string.Empty;
}
