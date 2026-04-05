namespace Alexandria.Api.Models.Responses;

public class AuthorDetailResponse
{
    public Guid AuthorUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<BookResponse> Books { get; set; } = [];
}
