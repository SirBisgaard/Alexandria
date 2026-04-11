using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IEpubMetadataHandler
{
    Book ExtractMetadata(string epubPath);
    byte[]? ExtractCoverBytes(string epubPath);
    (Book Book, string? CoverFilePath) ExtractAll(string epubPath, string coverOutputDirectory, Guid coverUuid);
    void EmbedMetadata(string epubPath, Book book);
    void EmbedCover(string epubPath, byte[] coverData, string mediaType);
    void RemoveCover(string epubPath);
}
