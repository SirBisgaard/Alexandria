using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Library.Interfaces;

public interface IEpubMetadataHandler
{
    Book ExtractMetadata(string epubPath);
    (Book Book, string? CoverFilePath) ExtractAll(string epubPath, string coverOutputDirectory);
    void EmbedMetadata(string epubPath, Book book);
    void EmbedCover(string epubPath, byte[] coverData, string mediaType);
    void RemoveCover(string epubPath);
}
