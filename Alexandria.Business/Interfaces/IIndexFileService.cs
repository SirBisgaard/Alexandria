using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IIndexFileService
{
    List<Book> Books { get; set; }
    Task LoadAsync();
    Task SaveAsync();
    Task<string> ComputeFileHashAsync(string filePath);
    Book? FindByHash(string fileHash);
    Book? FindByUuid(Guid uuid);
}
