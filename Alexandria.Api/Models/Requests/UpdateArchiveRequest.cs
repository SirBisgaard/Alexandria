namespace Alexandria.Api.Models.Requests;

public record UpdateArchiveRequest
{
    public bool IsArchived { get; init; }
}
