using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IAuthorRepository
{
    Task<List<Author>> GetAll();
    Task<Author?> FindByName(string name);
    Task<Guid> GetOrCreate(string name);
    Task SetBookAuthors(Guid bookUuid, List<string> authorNames);
    Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid);
    Task RemoveOrphanedAuthors();
}
