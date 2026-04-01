using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IFileRepository
{
    Task<byte[]?> ReadKepub(Book book);
    Task<byte[]?> ReadCover(Book book);
    void DeleteCover(Book book);
    void DeleteKepub(Book book);
    string[] GetEpubFiles();
    Task<string> ComputeFileHash(string filePath);
    long GetKepubFileSize(string kepubPath);
}
