using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Comics;

/// <summary>
/// Media handler for CBZ comics. Metadata round-trips through ComicInfo.xml; the cover is always
/// a page of the comic, so cover replacement is not supported. Kobo downloads are served from
/// the per-device renditions (see <see cref="Business.Interfaces.IComicRenditionStore"/>), not
/// through <see cref="ReadContent"/>.
/// </summary>
public class FileComicMediaHandler(ICbzFileHandler cbzFileHandler, IFileRepository fileRepository) : IFileContentMediaHandler
{
    public bool CanHandle(string path) => path.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase);

    public async Task<byte[]?> ReadContent(Book book) => File.Exists(book.File.SourcePath) ? await File.ReadAllBytesAsync(book.File.SourcePath) : null;

    public Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null) => fileRepository.ReadCover(book, width, height, quality);

    public Book ExtractMetadata(string filePath) => cbzFileHandler.ExtractMetadata(filePath);

    /// <summary>
    /// Fields ComicInfo cannot hold are copied onto the file's view first, so a value that only
    /// ever lives in the database never forces a needless archive rewrite.
    /// </summary>
    public bool MetadataEquals(Book desired, string filePath)
    {
        var current = cbzFileHandler.ExtractMetadata(filePath);
        PreservePropertiesNotStoredInFile(desired, current);
        return desired.MetadataEquals(current) && desired.File.ReadingDirection == current.File.ReadingDirection;
    }

    public void EmbedMetadata(Book book) => cbzFileHandler.EmbedMetadata(book.File.SourcePath, book);

    public void EmbedCover(Book book, byte[] coverData, string mediaType) => throw new NotSupportedException("A comic's cover is its first page and cannot be replaced.");

    public void RemoveCover(Book book) => throw new NotSupportedException("A comic's cover is its first page and cannot be removed.");

    public void PreservePropertiesNotStoredInFile(Book source, Book target)
    {
        target.Subtitle = source.Subtitle;
        target.Rights = source.Rights;
        target.Source = source.Source;
        target.Type = source.Type;
        target.Relation = source.Relation;
        target.Coverage = source.Coverage;
        target.Format = source.Format;
        target.Identifiers = source.Identifiers;
        target.File.ColorMode = source.File.ColorMode ?? ComicColorMode.Auto;
    }
}
