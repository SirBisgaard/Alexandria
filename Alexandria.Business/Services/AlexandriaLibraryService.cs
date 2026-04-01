using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class AlexandriaLibraryService(IBookRepository bookRepository) : IAlexandriaLibraryService
{
    public async Task<List<Book>> GetBooks()
    {
        return await bookRepository.GetAll();
    }

    public async Task<Book?> GetBookByUuid(Guid uuid)
    {
        return await bookRepository.FindByUuid(uuid);
    }

    public async Task ArchiveBook(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
        {
            return;
        }
        book.IsArchived = true;
        await bookRepository.Update(book);
    }
}
