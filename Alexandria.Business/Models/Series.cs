namespace Alexandria.Business.Models;

public class Series
{
    public Guid SeriesUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Index { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public List<Book> Books { get; set; } = [];
}
