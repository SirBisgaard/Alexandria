namespace Alexandria.Business.Library.Models.Others;

public record CollectionSortOptions
{
    public CollectionSortField Sort { get; init; } = CollectionSortField.CollectionName;
    public SortOrder Order { get; init; } = SortOrder.Asc;
}
