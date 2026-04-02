namespace Alexandria.Business.Models;

public class Series
{
    public Guid Uuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Book> Books { get; set; } = [];
}
