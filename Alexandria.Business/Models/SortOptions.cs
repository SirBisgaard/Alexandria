namespace Alexandria.Business.Models;

public enum SortOrder
{
    Asc,
    Desc
}

public enum BookSortField
{
    Recent,
    DateAdded,
    Title,
    Author,
    Series,
    FileSize,
    BookNumber
}

public enum AuthorSortField
{
    LastName,
    FirstName,
    Recent,
    NumberOfBooks
}

public enum SeriesSortField
{
    SeriesTitle,
    Recent,
    NumberOfBooks
}

public enum CollectionSortField
{
    CollectionName,
    NumberOfBooks,
    DateCreated
}

public record BookSortOptions(BookSortField Sort = BookSortField.Recent, SortOrder Order = SortOrder.Desc);

public record AuthorSortOptions(AuthorSortField Sort = AuthorSortField.LastName, SortOrder Order = SortOrder.Asc);

public record SeriesSortOptions(SeriesSortField Sort = SeriesSortField.SeriesTitle, SortOrder Order = SortOrder.Asc);

public record CollectionSortOptions(CollectionSortField Sort = CollectionSortField.CollectionName, SortOrder Order = SortOrder.Asc);
