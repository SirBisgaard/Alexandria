using Alexandria.Business.Models.Files;

namespace Alexandria.Business.Interfaces;

public interface IEpubMetadataHandler
{
    EpubMetadata ExtractMetadata(string epubPath);
    (EpubMetadata Metadata, string? CoverFilePath) ExtractAll(string epubPath, string coverOutputDirectory);
    void EmbedMetadata(string epubPath, EpubMetadata metadata);
    void EmbedCover(string epubPath, byte[] coverData, string mediaType);
    void RemoveCover(string epubPath);
}
