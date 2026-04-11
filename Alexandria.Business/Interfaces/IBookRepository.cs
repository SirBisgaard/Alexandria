using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Interfaces;

public interface IBookRepository
{
    Task<List<Book>> GetAll();
    Task<Book?> FindByUuid(Guid uuid);
    Task<Book?> FindByEntitlementUuid(Guid entitlementUuid);
    Task<Book?> FindByHash(string fileHash);
    Task<Book?> FindByPath(string sourcePath);
    Task Add(Book book);
    Task Update(Book book);
    Task Remove(Guid uuid);
    Task<List<string>> GetPublishers();
}
