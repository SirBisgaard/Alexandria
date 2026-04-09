using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Interfaces;

public interface ICollectionRepository
{
    Task<List<BookCollection>> GetAll();
    Task<BookCollection?> FindByUuid(Guid uuid);
    Task Upsert(BookCollection collection);
    Task Delete(Guid uuid);
    Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since);
}
