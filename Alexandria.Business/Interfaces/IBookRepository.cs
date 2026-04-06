using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IBookRepository
{
    Task<List<Book>> GetAll();
    Task<Book?> FindByUuid(Guid uuid);
    Task<Book?> FindByEntitlementUuid(Guid entitlementUuid);
    Task<Book?> FindByCoverImageUuid(Guid uuid);
    Task<Book?> FindByHash(string fileHash);
    Task Add(Book book);
    Task Update(Book book);
    Task Remove(Guid uuid);
}
