using Alexandria.Business.Models;

namespace Alexandria.Business.Interfaces;

public interface IEpubMetadataExstractor
{
    EpubMetadata Extract(string epubPath, string coverOutputDirectory);
}
