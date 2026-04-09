using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IFileContentMediaHandler
{
    string[] SupportedExtensions { get; }
    Task<byte[]?> ReadContent(Book book);
    Task<byte[]?> ReadChapter(Book book, int chapterIndex);
    Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null);
    Book ExtractMetadata(string filePath);
    bool MetadataEquals(Book desired, string filePath);
    void EmbedMetadata(string filePath, Book book);
    void EmbedCover(string filePath, byte[] coverData, string mediaType);
    void RemoveCover(string filePath);
}
