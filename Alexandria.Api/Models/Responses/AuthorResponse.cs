namespace Alexandria.Api.Models.Responses;

public class AuthorResponse
{
    public Guid AuthorUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
