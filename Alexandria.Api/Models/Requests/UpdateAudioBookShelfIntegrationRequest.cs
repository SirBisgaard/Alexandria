namespace Alexandria.Api.Models.Requests;

public record UpdateAudioBookShelfIntegrationRequest
{
    public bool Enabled { get; init; }
    public string ServerUrl { get; init; } = string.Empty;
    public string ApiToken { get; init; } = string.Empty;
}
