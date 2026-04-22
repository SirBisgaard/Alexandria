namespace Alexandria.Business.Library.Models.Others;

public sealed record FileManagementChapterRename
{
    public int ChapterIndex { get; init; }
    public string OldFileName { get; init; } = string.Empty;
    public string NewFileName { get; init; } = string.Empty;
}
