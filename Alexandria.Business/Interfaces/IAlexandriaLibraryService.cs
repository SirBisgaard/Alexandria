using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IAlexandriaLibraryService
{
    Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null);
    Task<Book?> GetBookByUuid(Guid uuid);
    Task<Book?> GetBookByEntitlementUuid(Guid entitlementUuid);
    Task<Book?> GetBookByCoverImageUuid(Guid uuid);
    Task<List<Author>> GetAuthors(AuthorSortOptions? sortOptions = null);
    Task<Author?> GetAuthorByName(string name, BookSortOptions? sortOptions = null);
    Task<List<Series>> GetSeries(SeriesSortOptions? sortOptions = null);
    Task<Series?> GetSeriesByUuid(Guid uuid, BookSortOptions? sortOptions = null);
    Task<byte[]?> GetBookCover(Guid uuid, int? width = null, int? height = null);
    Task SetBookArchiveStatus(Guid uuid, bool isArchived);
    Task<List<BookCollection>> GetCollections(CollectionSortOptions? sortOptions = null);
    Task<CollectionWithBooks?> GetCollectionWithBooks(Guid uuid, BookSortOptions? sortOptions = null);
    Task UpsertCollection(BookCollection collection);
    Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since);
    Task UpdateReadingStatus(Guid uuid, ReadingStatus status);
    Task<Book?> UpdateBookMetadata(Book book);
    Task<Book?> UpdateBookCover(Guid uuid, byte[] coverData, string mediaType);
    Task<Book?> RemoveBookCover(Guid uuid);
    Task<int> Sync(IProgress<(int current, int total)>? progress = null);
    Task<Author?> GetAuthorByUuid(Guid uuid, BookSortOptions? sortOptions = null);
    Task<Author?> UpdateAuthor(Author author);
    Task<Series?> UpdateSeries(Series series);
    Task<Series?> GetSeriesByName(string name);
}
