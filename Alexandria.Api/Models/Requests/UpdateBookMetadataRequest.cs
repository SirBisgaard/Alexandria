namespace Alexandria.Api.Models.Requests;

public class UpdateBookMetadataRequest
{
    public string Title { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? SeriesName { get; set; }
    public double? Index { get; set; }
}
