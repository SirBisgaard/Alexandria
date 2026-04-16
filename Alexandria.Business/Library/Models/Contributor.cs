namespace Alexandria.Business.Library.Models;

/// <summary>
/// A non-author contributor to a book (e.g. narrator, editor, translator).
/// For audiobooks, the only supported role is "Narrator".
/// </summary>
public class Contributor
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Free-form role string. Audiobooks only support "Narrator"; ebooks support any Dublin Core role.</summary>
    public string? Role { get; set; }
}
