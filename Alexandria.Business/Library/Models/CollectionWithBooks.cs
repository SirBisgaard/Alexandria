namespace Alexandria.Business.Library.Models;

/// <summary>
/// A collection with fully loaded book details. Used for collection detail views.
/// </summary>
public class CollectionWithBooks
{
    public Guid CollectionUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Book> Books { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
