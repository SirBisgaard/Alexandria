using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IAlexandriaLibraryService
{
    Task<List<Book>> GetBooks();
    Task<Book?> GetBookByUuid(Guid uuid);
    Task<List<Author>> GetAuthors();
    Task<Author?> GetAuthorByName(string name);
    Task<List<Series>> GetSeries();
    Task<Series?> GetSeriesByUuid(Guid uuid);
    Task<byte[]?> GetBookCover(Guid uuid);
    Task ArchiveBook(Guid uuid);
    Task<List<BookCollection>> GetCollections();
    Task<BookCollection?> GetCollectionByUuid(Guid uuid);
    Task<BookCollection> CreateOrUpdateCollection(string name, List<Guid> bookUuids);
    Task UpsertCollection(BookCollection collection);
    Task DeleteCollection(Guid uuid);
    Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since);
}
