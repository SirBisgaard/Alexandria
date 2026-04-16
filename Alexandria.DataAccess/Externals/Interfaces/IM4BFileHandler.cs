using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Externals.Interfaces;

public interface IM4BFileHandler
{
    Task<Book> ExtractMetadata(string filePath);
    Task<byte[]?> ExtractCoverBytes(string filePath);
    Task<string?> ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid);
    Task StreamChapter(string sourcePath, AudiobookChapter chapter, string? codec, int? bitrate, Stream outputStream, CancellationToken cancellationToken);
    Task EmbedMetadata(string filePath, Book book);
    Task EmbedCover(string filePath, byte[] coverData, string mediaType);
    Task RemoveCover(string filePath);
}
