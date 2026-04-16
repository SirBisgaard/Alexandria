using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileAudiobookMediaHandler(IM4BMetadataHandler m4BMetadataHandler) : IFileContentMediaHandler
{
    public string[] SupportedExtensions => [".m4b"];

    public Task<byte[]?> ReadContent(Book book) => throw new NotSupportedException();

    public async Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken)
    {
        if (chapterIndex < 0 || chapterIndex >= book.Chapters.Count)
            throw new ArgumentOutOfRangeException(nameof(chapterIndex));

        var chapter = book.Chapters[chapterIndex];
        await m4BMetadataHandler.StreamChapter(book.File.SourcePath, chapter, book.File.AudioCodec, outputStream, cancellationToken);
    }

    public async Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null)
    {
        if (book.File.CoverFilePath is null || !File.Exists(book.File.CoverFilePath))
            return null;

        return await File.ReadAllBytesAsync(book.File.CoverFilePath);
    }

    public Book ExtractMetadata(string filePath) => m4BMetadataHandler.ExtractMetadata(filePath).GetAwaiter().GetResult();

    public byte[]? ExtractCoverBytes(string filePath) => m4BMetadataHandler.ExtractCoverBytes(filePath).GetAwaiter().GetResult();

    public bool MetadataEquals(Book desired, string filePath)
    {
        var current = ExtractMetadata(filePath);
        return desired.MetadataEquals(current);
    }

    public void EmbedMetadata(string filePath, Book book) => m4BMetadataHandler.EmbedMetadata(filePath, book).GetAwaiter().GetResult();

    public void EmbedCover(string filePath, byte[] coverData, string mediaType) => m4BMetadataHandler.EmbedCover(filePath, coverData, mediaType).GetAwaiter().GetResult();

    public void RemoveCover(string filePath) => m4BMetadataHandler.RemoveCover(filePath).GetAwaiter().GetResult();

    public string? ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid) => m4BMetadataHandler.ExtractCover(filePath, coverOutputDirectory, coverUuid).GetAwaiter().GetResult();
}
