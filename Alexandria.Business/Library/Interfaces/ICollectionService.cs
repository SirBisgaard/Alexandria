using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

public interface ICollectionService
{
    Task<List<BookCollection>> GetCollections(CollectionSortOptions? sortOptions = null);
    Task<CollectionWithBooks?> GetCollectionWithBooks(Guid uuid, BookSortOptions? sortOptions = null);
    Task UpsertCollection(BookCollection collection);
    Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since);
}
