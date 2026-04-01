using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IAlexandriaLibraryService
{
    Task<List<Book>> GetBooks();
    Task<Book?> GetBookByUuid(Guid uuid);
    Task ArchiveBook(Guid uuid);
}
