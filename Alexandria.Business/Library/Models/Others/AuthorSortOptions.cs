namespace Alexandria.Business.Library.Models.Others;

public record AuthorSortOptions
{
    public AuthorSortField Sort { get; init; } = AuthorSortField.LastName;
    public SortOrder Order { get; init; } = SortOrder.Asc;
}
