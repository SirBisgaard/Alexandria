using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileAudiobookMediaHandler(IM4BMetadataHandler m4BMetadataHandler) : IFileContentMediaHandler
{
    public string[] SupportedExtensions => [".m4b"];

    public Task<byte[]?> ReadContent(Book book) => throw new NotSupportedException();

    public async Task<byte[]?> ReadChapter(Book book, int chapterIndex)
    {
        if (chapterIndex < 0 || chapterIndex >= book.Chapters.Count)
            return null;

        var chapter = book.Chapters[chapterIndex];

        return await m4BMetadataHandler.ExtractChapterAsMp3(book.SourcePath, chapter);
    }

    public async Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null)
    {
        if (book.CoverFilePath is null || !File.Exists(book.CoverFilePath))
            return null;

        return await File.ReadAllBytesAsync(book.CoverFilePath);
    }

    public Book ExtractMetadata(string filePath) => m4BMetadataHandler.ExtractMetadata(filePath).GetAwaiter().GetResult();

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
