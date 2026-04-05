namespace Alexandria.Business.Models;

public class BookCollection
{
    public Guid CollectionUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Guid> BookUuids { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModified { get; set; }
}
