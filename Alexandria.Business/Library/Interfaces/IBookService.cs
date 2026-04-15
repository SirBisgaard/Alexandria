using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

public interface IBookService
{
    Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null);
    Task<Book?> GetBookByUuid(Guid uuid);
    Task<Book?> GetBookByEntitlementUuid(Guid entitlementUuid);
    Task<byte[]?> GetBookCover(Guid uuid, int? width = null, int? height = null);
    Task SetBookArchiveStatus(Guid uuid, bool isArchived);
    Task UpdateReadingStatus(Guid uuid, ReadingStatus status);
    Task<Book?> UpdateBookMetadata(Book book);
    Task<Book?> UpdateBookCover(Guid uuid, byte[] coverData, string mediaType);
    Task<Book?> RemoveBookCover(Guid uuid);
    Task<List<Book>> GetRecommendations(Guid bookUuid, int count = 5);
    Task<List<string>> GetPublishers();
}
