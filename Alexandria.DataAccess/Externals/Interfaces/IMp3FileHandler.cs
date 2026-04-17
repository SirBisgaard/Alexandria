using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Externals.Interfaces;

public interface IMp3FileHandler
{
    Task<Book> ExtractMetadata(string folderPath);
    Task<byte[]?> ExtractCoverBytes(string folderPath);
    Task<(Book Metadata, byte[]? CoverBytes)> Extract(string folderPath);
    Task<string?> ExtractCover(string folderPath, string coverOutputDirectory, Guid coverUuid);
    Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken);
    Task EmbedMetadata(Book book, byte[]? coverBytes = null);
    Task EmbedCover(Book book, byte[] coverData);
    Task RemoveCover(Book book);
}
