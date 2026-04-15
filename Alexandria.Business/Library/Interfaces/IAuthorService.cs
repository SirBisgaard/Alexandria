using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Interfaces;

public interface IAuthorService
{
    Task<List<Author>> GetAuthors(AuthorSortOptions? sortOptions = null);
    Task<Author?> GetAuthorByUuid(Guid uuid, BookSortOptions? sortOptions = null);
    Task<Author?> GetAuthorByName(string name, BookSortOptions? sortOptions = null);
    Task<Author?> UpdateAuthor(Author author);
    Task<Author?> MergeAuthor(Guid sourceUuid, Guid targetUuid);
}
