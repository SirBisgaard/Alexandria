namespace Alexandria.Business.Library.Models;

/// <summary>
/// A book series. Series are deduplicated by name — renaming merges all books into the target.
/// </summary>
public class Series
{
    /// <summary>Deterministic UUID v5 derived from the series name.</summary>
    public Guid SeriesUuid { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>This book's position in the series (e.g. 1, 2.5). Null if unknown. Context-dependent — only meaningful when loaded via a specific book.</summary>
    public decimal? Index { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>Books in this series. Only populated in detail views (e.g. series detail endpoint).</summary>
    public List<Book> Books { get; set; } = [];
}
