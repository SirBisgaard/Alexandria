using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IM4BMetadataHandler
{
    Task<Book> ExtractMetadata(string filePath);
    Task<byte[]?> ExtractCoverBytes(string filePath);
    Task<string?> ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid);
    Task StreamChapter(string sourcePath, AudiobookChapter chapter, string? codec, Stream outputStream, CancellationToken cancellationToken);
    Task EmbedMetadata(string filePath, Book book);
    Task EmbedCover(string filePath, byte[] coverData, string mediaType);
    Task RemoveCover(string filePath);
}
