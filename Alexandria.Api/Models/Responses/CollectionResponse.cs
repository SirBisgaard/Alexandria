namespace Alexandria.Api.Models.Responses;

public class CollectionResponse
{
    public Guid CollectionUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public int EbookCount { get; set; }
    public int AudiobookCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
