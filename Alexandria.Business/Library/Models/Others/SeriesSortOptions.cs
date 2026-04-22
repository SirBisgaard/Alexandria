namespace Alexandria.Business.Library.Models.Others;

public record SeriesSortOptions
{
    public SeriesSortField Sort { get; init; } = SeriesSortField.SeriesTitle;
    public SortOrder Order { get; init; } = SortOrder.Asc;
}
