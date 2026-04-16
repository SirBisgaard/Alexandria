namespace Alexandria.Api.Models.Responses;

public class BookResponse
{
    public Guid BookUuid { get; set; }
    public Guid EntitlementUuid { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public List<AuthorResponse> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? Isbn { get; set; }
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public List<string> Identifiers { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
    public List<ContributorResponse> Contributors { get; set; } = [];
    public string? Rights { get; set; }
    public string? Source { get; set; }
    public string? Type { get; set; }
    public string? Relation { get; set; }
    public string? Coverage { get; set; }
    public string? Format { get; set; }
    public SeriesResponse? Series { get; set; }
    public BookFileResponse File { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public bool IsArchived { get; set; }
    public ReadingProgressResponse? ReadingProgress { get; set; }
    public List<AudiobookChapterResponse> Chapters { get; set; } = [];
}
