using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IEpubMetadataExtractor
{
    EpubMetadata ExtractMetadata(string epubPath);
    string? ExtractCover(string epubPath, string coverOutputDirectory);
}
