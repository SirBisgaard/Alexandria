namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// A self-describing move. <see cref="OldSourcePath"/> / <see cref="NewSourcePath"/> are the
/// real on-disk paths — EPUB/M4B files carry their extension, MP3-folder audiobooks are
/// directory paths.
/// </summary>
public sealed record FileRenameStep
{
    public string OldSourcePath { get; init; } = string.Empty;
    public string NewSourcePath { get; init; } = string.Empty;
    public IReadOnlyList<ChapterFileRename> ChapterRenames { get; init; } = [];

    public bool IsNoOperation => string.Equals(OldSourcePath, NewSourcePath, StringComparison.Ordinal) && ChapterRenames.Count == 0;

    public FileRenameStep Invert()
    {
        var inverted = new List<ChapterFileRename>(ChapterRenames.Count);
        foreach (var rename in ChapterRenames)
            inverted.Add(rename.Invert());
        return new FileRenameStep
        {
            OldSourcePath = NewSourcePath,
            NewSourcePath = OldSourcePath,
            ChapterRenames = inverted
        };
    }
}
