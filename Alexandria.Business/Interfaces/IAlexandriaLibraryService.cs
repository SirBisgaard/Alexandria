using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IAlexandriaLibraryService
{
    Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null);
    Task<Book?> GetBookByUuid(Guid uuid);
    Task<Book?> GetBookByEntitlementUuid(Guid entitlementUuid);
    Task<List<Author>> GetAuthors(AuthorSortOptions? sortOptions = null);
    Task<Author?> GetAuthorByName(string name, BookSortOptions? sortOptions = null);
    Task<List<Series>> GetSeries(SeriesSortOptions? sortOptions = null);
    Task<Series?> GetSeriesByUuid(Guid uuid, BookSortOptions? sortOptions = null);
    Task<byte[]?> GetBookCover(Guid uuid, int? width = null, int? height = null);
    Task SetBookArchiveStatus(Guid uuid, bool isArchived);
    Task<List<BookCollection>> GetCollections(CollectionSortOptions? sortOptions = null);
    Task<BookCollection?> GetCollectionByUuid(Guid uuid);
    Task<CollectionWithBooks?> GetCollectionWithBooks(Guid uuid, BookSortOptions? sortOptions = null);
    Task<BookCollection> CreateOrUpdateCollection(string name, List<Guid> bookUuids);
    Task UpsertCollection(BookCollection collection);
    Task DeleteCollection(Guid uuid);
    Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since);
    Task UpdateReadingStatus(Guid uuid, string status);
    Task<Book?> UpdateBookMetadata(Guid uuid, Book book);
    Task<Book?> UpdateBookCover(Guid uuid, byte[] coverData, string mediaType);
    Task<Book?> RemoveBookCover(Guid uuid);
    Task<int> Sync(IProgress<(int current, int total)>? progress = null);
}
