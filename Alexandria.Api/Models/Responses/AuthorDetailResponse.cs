namespace Alexandria.Api.Models.Responses;

public class AuthorDetailResponse
{
    public Guid AuthorUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public List<BookResponse> Books { get; set; } = [];
}
