using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Externals.Interfaces;

public interface IM4BFileHandler
{
    Task<Book> ExtractMetadata(string filePath);
    Task<byte[]?> ExtractCoverBytes(string filePath);
    Task<string?> ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid);
    Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken);
    Task TranscodeChapterToFile(Book book, int chapterIndex, string outputPath, CancellationToken cancellationToken);
    Task EmbedMetadata(Book book);
    Task EmbedCover(Book book, byte[] coverData);
    Task RemoveCover(Book book);
}
