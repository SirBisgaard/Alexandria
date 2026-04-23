namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// A self-describing move. <see cref="OldSourcePath"/> / <see cref="NewSourcePath"/> use the
/// same virtualized form stored in <c>BookFiles.SourcePath</c> — that is, for MP3-folder
/// audiobooks these include the <c>.mp3audiobook</c> suffix; the renamer strips it internally.
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
