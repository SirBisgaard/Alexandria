namespace Alexandria.Business.Models.KoboStore;

public record SeriesInfo
{
    public required string Name { get; set; }
    public required int Number { get; set; }
    public required float NumberFloat { get; set; }
    public required string Id { get; set; }
}