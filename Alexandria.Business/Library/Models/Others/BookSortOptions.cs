namespace Alexandria.Business.Library.Models.Others;

public record BookSortOptions
{
    public BookSortField Sort { get; init; } = BookSortField.Recent;
    public SortOrder Order { get; init; } = SortOrder.Desc;
}
