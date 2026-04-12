namespace Alexandria.Api.Models.Requests;

public class ContributorRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
}
