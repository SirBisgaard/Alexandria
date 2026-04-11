using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IFileRepository
{
    Task<byte[]?> ReadKepub(Book book);
    Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null);
    void DeleteCover(Book book);
    void DeleteKepub(Book book);
    string[] GetEpubFiles();
    long GetFileSize(string path);
}
