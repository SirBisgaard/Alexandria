using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IBookCollectionRepository
{
    Task<List<BookCollection>> GetAll();
    Task<BookCollection?> FindByUuid(Guid uuid);
    Task Upsert(BookCollection collection);
    Task Delete(Guid uuid);
    Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since);
}
