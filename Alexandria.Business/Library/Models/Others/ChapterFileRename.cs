namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// A single chapter-file rename within an MP3-folder audiobook. Empty for single-file formats.
/// </summary>
public sealed record ChapterFileRename
{
    public int ChapterIndex { get; init; }
    public string OldFileName { get; init; } = string.Empty;
    public string NewFileName { get; init; } = string.Empty;

    public ChapterFileRename Invert() => new()
    {
        ChapterIndex = ChapterIndex,
        OldFileName = NewFileName,
        NewFileName = OldFileName
    };
}
