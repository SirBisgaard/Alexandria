using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Interfaces;

public interface IFileContentManager
{
    int GetBookFilesCount();
    IAsyncEnumerable<(Book Metadata, byte[]? CoverBytes)> GetAllBooks();
    string? SaveCover(string filePath, byte[]? coverBytes, Guid bookUuid);
    void CleanupStaleDerivatives(List<Book> currentBooks, List<Book>? previousBooks);
    Task CreateDerivativeFiles(Book book);
    Task<Book> GetBook(Book book);
    Task<Book> UpdateBook(Book book, CoverUpdate? coverUpdate = null);
    Task<byte[]?> GetContentFile(Book book);
    Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken);
    Task<byte[]?> GetCoverFile(Book book, int? width = null, int? height = null, int? quality = null);
}
