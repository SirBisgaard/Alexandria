namespace Alexandria.Business.Library.Models;

/// <summary>
/// Page-turn direction of a comic. Stored in the CBZ's ComicInfo.xml <c>Manga</c> element
/// (<c>YesAndRightToLeft</c> means right-to-left) and written into the generated EPUB spine
/// as <c>page-progression-direction</c>.
/// </summary>
public enum ComicReadingDirection
{
    LeftToRight,
    RightToLeft
}
