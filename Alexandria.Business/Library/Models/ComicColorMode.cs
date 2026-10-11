namespace Alexandria.Business.Library.Models;

/// <summary>
/// How comic pages are rendered for colour-capable Kobo screens. Black-and-white screens always
/// receive grayscale pages regardless of this setting. Not representable in ComicInfo.xml, so it
/// lives in the database only.
/// </summary>
public enum ComicColorMode
{
    /// <summary>Each page is scanned and kept in colour only when it actually contains colour.</summary>
    Auto,

    /// <summary>Every page keeps its colour on colour screens.</summary>
    Color,

    /// <summary>Every page is converted to grayscale.</summary>
    Grayscale
}
