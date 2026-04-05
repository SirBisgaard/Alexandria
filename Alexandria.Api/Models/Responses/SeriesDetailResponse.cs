namespace Alexandria.Api.Models.Responses;

public class SeriesDetailResponse
{
    public Guid SeriesUuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<BookResponse> Books { get; set; } = [];
}
