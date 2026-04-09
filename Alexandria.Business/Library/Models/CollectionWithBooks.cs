namespace Alexandria.Business.Library.Models;

public class CollectionWithBooks
{
    public Guid CollectionUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Book> Books { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
