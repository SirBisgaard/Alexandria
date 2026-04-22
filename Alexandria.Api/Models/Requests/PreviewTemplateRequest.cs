namespace Alexandria.Api.Models.Requests;

public record PreviewTemplateRequest
{
    public string Template { get; init; } = string.Empty;
}
