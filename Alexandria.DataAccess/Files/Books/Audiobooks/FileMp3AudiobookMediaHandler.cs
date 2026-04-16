using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileMp3AudiobookMediaHandler(IMp3FileHandler mp3FileHandler) : IFileContentMediaHandler
{
    public string[] SupportedExtensions => ["__.mp3"];

    public Task<byte[]?> ReadContent(Book book) => throw new NotSupportedException();

    public async Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken)
    {
        if (chapterIndex < 0 || chapterIndex >= book.Chapters.Count)
            throw new ArgumentOutOfRangeException(nameof(chapterIndex));

        await mp3FileHandler.StreamChapter(book, chapterIndex, outputStream, cancellationToken);
    }

    public async Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null)
    {
        if (book.File.CoverFilePath is null || !File.Exists(book.File.CoverFilePath))
            return null;

        return await File.ReadAllBytesAsync(book.File.CoverFilePath);
    }

    public Book ExtractMetadata(string filePath) => mp3FileHandler.ExtractMetadata(ToFolderPath(filePath)).GetAwaiter().GetResult();

    public byte[]? ExtractCoverBytes(string filePath) => mp3FileHandler.ExtractCoverBytes(ToFolderPath(filePath)).GetAwaiter().GetResult();

    public bool MetadataEquals(Book desired, string filePath)
    {
        var current = ExtractMetadata(filePath);
        return desired.MetadataEquals(current);
    }

    public void EmbedMetadata(Book book) => mp3FileHandler.EmbedMetadata(book).GetAwaiter().GetResult();

    public void EmbedCover(Book book, byte[] coverData, string mediaType) => mp3FileHandler.EmbedCover(book, coverData, mediaType).GetAwaiter().GetResult();

    public void RemoveCover(Book book) => mp3FileHandler.RemoveCover(book).GetAwaiter().GetResult();

    public string? ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid) => mp3FileHandler.ExtractCover(ToFolderPath(filePath), coverOutputDirectory, coverUuid).GetAwaiter().GetResult();

    private static string ToFolderPath(string virtualPath) => virtualPath[..^"__.mp3".Length];
}
