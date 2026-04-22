namespace Alexandria.Api.Models.Requests;

public record UpdateUserRequest
{
    public string Username { get; init; } = string.Empty;
    public string? Password { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}
