using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class AlexandriaLibraryService(IIndexFileService indexFileService) : IAlexandriaLibraryService
{
    public async Task<List<Book>> GetBooksAsync()
    {
        await indexFileService.LoadAsync();
        return indexFileService.Books;
    }
}
