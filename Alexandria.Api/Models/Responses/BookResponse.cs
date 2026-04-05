namespace Alexandria.Api.Models.Responses;

public class BookResponse
{
    public Guid BookUuid { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<AuthorResponse> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public SeriesResponse? Series { get; set; }
    public bool HasCover { get; set; }
    public long KepubFileSize { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public bool IsArchived { get; set; }
    public ReadingProgressResponse? ReadingProgress { get; set; }
}
