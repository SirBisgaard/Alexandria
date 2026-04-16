using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileAudiobookMediaHandler(IM4BFileHandler im4BFileHandler) : IFileContentMediaHandler
{
    public string[] SupportedExtensions => [".m4b"];

    public Task<byte[]?> ReadContent(Book book) => throw new NotSupportedException();

    public async Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken)
    {
        if (chapterIndex < 0 || chapterIndex >= book.Chapters.Count)
            throw new ArgumentOutOfRangeException(nameof(chapterIndex));

        var chapter = book.Chapters[chapterIndex];
        await im4BFileHandler.StreamChapter(book.File.SourcePath, chapter, book.File.AudioCodec, book.File.AudioBitrate, outputStream, cancellationToken);
    }

    public async Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null)
    {
        if (book.File.CoverFilePath is null || !File.Exists(book.File.CoverFilePath))
            return null;

        return await File.ReadAllBytesAsync(book.File.CoverFilePath);
    }

    public Book ExtractMetadata(string filePath) => im4BFileHandler.ExtractMetadata(filePath).GetAwaiter().GetResult();

    public byte[]? ExtractCoverBytes(string filePath) => im4BFileHandler.ExtractCoverBytes(filePath).GetAwaiter().GetResult();

    public bool MetadataEquals(Book desired, string filePath)
    {
        var current = ExtractMetadata(filePath);
        return desired.MetadataEquals(current);
    }

    public void EmbedMetadata(string filePath, Book book) => im4BFileHandler.EmbedMetadata(filePath, book).GetAwaiter().GetResult();

    public void EmbedCover(string filePath, byte[] coverData, string mediaType) => im4BFileHandler.EmbedCover(filePath, coverData, mediaType).GetAwaiter().GetResult();

    public void RemoveCover(string filePath) => im4BFileHandler.RemoveCover(filePath).GetAwaiter().GetResult();

    public string? ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid) => im4BFileHandler.ExtractCover(filePath, coverOutputDirectory, coverUuid).GetAwaiter().GetResult();
}
