using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Interfaces;

public interface IAuthorRepository
{
    Task<List<Author>> GetAll();
    Task<Author?> FindByUuid(Guid authorUuid);
    Task<Author?> FindByName(string name);
    Task<Guid> GetOrCreate(string name);
    Task SetBookAuthors(Guid bookUuid, List<string> authorNames);
    Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid);
    Task<Dictionary<Guid, List<Author>>> GetAuthorsForAllBooks();
    Task RemoveOrphanedAuthors();
    Task Update(Author author);
    Task ReassignBooks(Guid sourceUuid, Guid targetUuid);
}
