using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IFileContentMediaHandler
{
    string[] SupportedExtensions { get; }
    Task<byte[]?> ReadContent(Book book);
    Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken);
    Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null);
    Book ExtractMetadata(string filePath);
    bool MetadataEquals(Book desired, string filePath);
    void EmbedMetadata(Book book);
    void EmbedCover(Book book, byte[] coverData, string mediaType);
    void RemoveCover(Book book);

    /// <summary>
    /// Copies Book properties that this file format cannot represent from <paramref name="source"/>
    /// onto <paramref name="target"/>. Called after a file is re-read following a write, so the
    /// returned book carries file-derived fields plus DB-only fields the format can't hold.
    /// </summary>
    void PreservePropertiesNotStoredInFile(Book source, Book target);
}
