namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// A single intended filesystem rename. <see cref="CurrentPath"/> / <see cref="NewPath"/> are
/// the real on-disk paths — EPUB/M4B files carry their extension, MP3-folder audiobooks are
/// directory paths.
/// </summary>
public sealed record FileManagementOperation
{
    public Guid BookUuid { get; init; }
    public string Title { get; init; } = string.Empty;
    public MediaType MediaType { get; init; }
    public string CurrentPath { get; init; } = string.Empty;
    public string NewPath { get; init; } = string.Empty;
    public string NewBaseName { get; init; } = string.Empty;
    public IReadOnlyList<FileManagementChapterRename> ChapterRenames { get; init; } = [];
}
