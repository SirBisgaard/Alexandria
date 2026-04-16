using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Helpers;

public static class AlexandriaLibraryHelper
{
    public static List<Book> SortBooks(List<Book> books, BookSortOptions options)
    {
        var desc = options.Order == SortOrder.Desc;
        return options.Sort switch
        {
            BookSortField.DateAdded => desc ? books.OrderByDescending(b => b.CreatedAt).ToList() : books.OrderBy(b => b.CreatedAt).ToList(),
            BookSortField.Title => desc ? books.OrderByDescending(b => b.Title).ToList() : books.OrderBy(b => b.Title).ToList(),
            BookSortField.Author => desc ? books.OrderByDescending(b => b.Authors.FirstOrDefault()?.Name ?? "").ThenByDescending(b => b.Title).ToList() : books.OrderBy(b => b.Authors.FirstOrDefault()?.Name ?? "").ThenBy(b => b.Title).ToList(),
            BookSortField.Series => desc ? books.OrderBy(b => b.Series is null ? 1 : 0).ThenByDescending(b => b.Series?.Name).ThenByDescending(b => b.Series?.Index ?? 0).ToList() : books.OrderBy(b => b.Series is null ? 1 : 0).ThenBy(b => b.Series?.Name).ThenBy(b => b.Series?.Index ?? 0).ToList(),
            BookSortField.FileSize => desc ? books.OrderByDescending(b => b.File.TotalFileSize).ToList() : books.OrderBy(b => b.File.TotalFileSize).ToList(),
            BookSortField.BookNumber => desc ? books.OrderByDescending(b => b.Series?.Index ?? 0).ToList() : books.OrderBy(b => b.Series?.Index ?? 0).ToList(),
            _ => desc ? books.OrderByDescending(b => b.ModifiedAt).ToList() : books.OrderBy(b => b.ModifiedAt).ToList()
        };
    }

    public static List<Author> SortAuthors(List<Author> authors, AuthorSortOptions options)
    {
        var desc = options.Order == SortOrder.Desc;
        return options.Sort switch
        {
            AuthorSortField.FirstName => desc ? authors.OrderByDescending(a => a.Name.Split(' ').First()).ToList() : authors.OrderBy(a => a.Name.Split(' ').First()).ToList(),
            AuthorSortField.Recent => desc ? authors.OrderByDescending(a => a.Books.Max(b => b.ModifiedAt)).ToList() : authors.OrderBy(a => a.Books.Max(b => b.ModifiedAt)).ToList(),
            AuthorSortField.NumberOfBooks => desc ? authors.OrderByDescending(a => a.Books.Count).ThenBy(a => a.Name).ToList() : authors.OrderBy(a => a.Books.Count).ThenBy(a => a.Name).ToList(),
            _ => desc ? authors.OrderByDescending(a => a.Name).ToList() : authors.OrderBy(a => a.Name).ToList()
        };
    }

    public static List<Series> SortSeries(List<Series> series, SeriesSortOptions options)
    {
        var desc = options.Order == SortOrder.Desc;
        return options.Sort switch
        {
            SeriesSortField.Recent => desc ? series.OrderByDescending(s => s.Books.Max(b => b.ModifiedAt)).ToList() : series.OrderBy(s => s.Books.Max(b => b.ModifiedAt)).ToList(),
            SeriesSortField.NumberOfBooks => desc ? series.OrderByDescending(s => s.Books.Count).ThenBy(s => s.Name).ToList() : series.OrderBy(s => s.Books.Count).ThenBy(s => s.Name).ToList(),
            _ => desc ? series.OrderByDescending(s => s.Name).ToList() : series.OrderBy(s => s.Name).ToList()
        };
    }

    public static List<BookCollection> SortCollections(List<BookCollection> collections, CollectionSortOptions options)
    {
        var desc = options.Order == SortOrder.Desc;
        return options.Sort switch
        {
            CollectionSortField.NumberOfBooks => desc ? collections.OrderByDescending(c => c.BookUuids.Count).ThenBy(c => c.Name).ToList() : collections.OrderBy(c => c.BookUuids.Count).ThenBy(c => c.Name).ToList(),
            CollectionSortField.DateCreated => desc ? collections.OrderByDescending(c => c.CreatedAt).ToList() : collections.OrderBy(c => c.CreatedAt).ToList(),
            _ => desc ? collections.OrderByDescending(c => c.Name).ToList() : collections.OrderBy(c => c.Name).ToList()
        };
    }
}
