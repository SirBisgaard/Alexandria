using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Externals.Interfaces;

public interface ICbzFileHandler
{
    (Book Metadata, byte[]? CoverBytes) Extract(string cbzPath);
    Book ExtractMetadata(string cbzPath);
    void EmbedMetadata(string cbzPath, Book book);
}
