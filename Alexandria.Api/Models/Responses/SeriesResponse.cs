namespace Alexandria.Api.Models.Responses;

public class SeriesResponse
{
    public Guid SeriesUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? Index { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
