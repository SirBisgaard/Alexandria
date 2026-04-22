using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Helpers;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Services;

public class CollectionService(ICollectionRepository collectionRepository, IBookService bookService) : ICollectionService
{
    public async Task<List<BookCollection>> GetCollections(CollectionSortOptions? sortOptions = null)
    {
        var collections = await collectionRepository.GetAll();
        return AlexandriaLibraryHelper.SortCollections(collections, sortOptions ?? new CollectionSortOptions());
    }

    public async Task<CollectionWithBooks?> GetCollectionWithBooks(Guid uuid, BookSortOptions? sortOptions = null)
    {
        var collection = await collectionRepository.FindByUuid(uuid);
        if (collection is null)
        {
            return null;
        }
        var allBooks = await bookService.GetBooks();
        var uuidSet = new HashSet<Guid>(collection.BookUuids);
        var books = allBooks.Where(b => uuidSet.Contains(b.BookUuid)).ToList();
        return new CollectionWithBooks { CollectionUuid = collection.CollectionUuid, Name = collection.Name, Books = AlexandriaLibraryHelper.SortBooks(books, sortOptions ?? new BookSortOptions { Sort = BookSortField.Title, Order = SortOrder.Asc }), CreatedAt = collection.CreatedAt, ModifiedAt = collection.ModifiedAt };
    }

    public async Task UpsertCollection(BookCollection collection) => await collectionRepository.Upsert(collection);

    public async Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since) => await collectionRepository.GetModifiedSince(since);
}
