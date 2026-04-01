using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class AlexandriaLibraryService(IIndexFileService indexFileService) : IAlexandriaLibraryService
{
    public async Task<List<Book>> GetBooks()
    {
        await indexFileService.LoadAsync();
        return indexFileService.Books;
    }

    public async Task<Book?> GetBookByUuid(Guid uuid)
    {
        await indexFileService.LoadAsync();
        return indexFileService.FindByUuid(uuid);
    }

    public async Task ArchiveBook(Guid uuid)
    {
        await indexFileService.LoadAsync();
        var book = indexFileService.FindByUuid(uuid);
        if (book is null) return;
        book.IsArchived = true;
        await indexFileService.SaveAsync();
    }
}
