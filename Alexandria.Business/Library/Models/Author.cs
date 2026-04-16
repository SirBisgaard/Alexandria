namespace Alexandria.Business.Library.Models;

/// <summary>
/// A book author. Authors are deduplicated by name — multiple books by the same author share one record.
/// </summary>
public class Author
{
    public Guid AuthorUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>Books by this author. Only populated in detail views (e.g. author detail endpoint).</summary>
    public List<Book> Books { get; set; } = [];
}
