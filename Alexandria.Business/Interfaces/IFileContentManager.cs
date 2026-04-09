using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Interfaces;

public interface IFileContentManager
{
    Task<List<Book>> GetAllBooks(List<Book>? existingBooks = null, IProgress<(int current, int total)>? progress = null);
    Task<Book> GetBook(Book book);
    Task<Book> UpdateBook(Book book, CoverUpdate? coverUpdate = null);
    Task<byte[]?> GetContentFile(Book book);
    Task<byte[]?> GetChapterFile(Book book, int chapterIndex);
    Task<byte[]?> GetCoverFile(Book book, int? width = null, int? height = null, int? quality = null);
}
