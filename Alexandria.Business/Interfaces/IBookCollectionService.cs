using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IBookCollectionService
{
    Task<List<BookCollection>> GetAll();
    Task<BookCollection?> GetByUuid(Guid uuid);
    Task<BookCollection> CreateOrUpdate(string name, List<Guid> bookUuids);
    Task Upsert(BookCollection collection);
    Task Delete(Guid uuid);
    Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since);
}
