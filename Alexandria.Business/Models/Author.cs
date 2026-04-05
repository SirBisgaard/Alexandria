namespace Alexandria.Business.Models;

public class Author
{
    public Guid AuthorUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Book> Books { get; set; } = [];
}
