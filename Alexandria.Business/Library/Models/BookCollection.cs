namespace Alexandria.Business.Library.Models;

/// <summary>
/// A user-defined collection (shelf) grouping books. Tracks book membership by UUID.
/// </summary>
public class BookCollection
{
    public Guid CollectionUuid { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>UUIDs of books in this collection. Book details are not loaded — use CollectionWithBooks for that.</summary>
    public List<Guid> BookUuids { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
