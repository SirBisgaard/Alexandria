using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileM4BAudiobookMediaHandler(IM4BFileHandler m4BFileHandler, AlexandriaSettings settings) : IFileContentMediaHandler
{
    public bool CanHandle(string path) => path.EndsWith(".m4b", StringComparison.OrdinalIgnoreCase);

    public Task<byte[]?> ReadContent(Book book) => throw new NotSupportedException();

    /// <summary>
    /// Serves a pre-transcoded chapter MP3 from <see cref="AlexandriaSettings.ChaptersFolder"/> when
    /// one exists (produced by <see cref="M4BFileTreatment.TranscodeAtIndex"/>), otherwise falls
    /// back to on-demand transcoding via ffmpeg. The fallback keeps <see cref="M4BFileTreatment.Nothing"/>
    /// working unchanged and tolerates partial pre-transcodes mid-index-run.
    /// </summary>
    public async Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken)
    {
        if (chapterIndex < 0 || chapterIndex >= book.Chapters.Count)
            throw new ArgumentOutOfRangeException(nameof(chapterIndex));

        var cachedPath = Path.Combine(settings.ChaptersFolder, book.BookUuid.ToString(), $"{chapterIndex}.mp3");
        if (File.Exists(cachedPath))
        {
            await using var source = new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await source.CopyToAsync(outputStream, cancellationToken);
            return;
        }

        await m4BFileHandler.StreamChapter(book, chapterIndex, outputStream, cancellationToken);
    }

    public async Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null)
    {
        if (book.File.CoverFilePath is null || !File.Exists(book.File.CoverFilePath))
            return null;

        return await File.ReadAllBytesAsync(book.File.CoverFilePath);
    }

    public Book ExtractMetadata(string filePath) => m4BFileHandler.ExtractMetadata(filePath).GetAwaiter().GetResult();

    public byte[]? ExtractCoverBytes(string filePath) => m4BFileHandler.ExtractCoverBytes(filePath).GetAwaiter().GetResult();

    public bool MetadataEquals(Book desired, string filePath)
    {
        var current = ExtractMetadata(filePath);
        return desired.MetadataEquals(current);
    }

    public void EmbedMetadata(Book book) => m4BFileHandler.EmbedMetadata(book).GetAwaiter().GetResult();

    public void EmbedCover(Book book, byte[] coverData, string mediaType) => m4BFileHandler.EmbedCover(book, coverData).GetAwaiter().GetResult();

    public void RemoveCover(Book book) => m4BFileHandler.RemoveCover(book).GetAwaiter().GetResult();

    public void PreservePropertiesNotStoredInFile(Book source, Book target)
    {
        // Carried over because the MP4 muxer has no tag for them — the write emits nothing
        // and the re-read returns default. Publisher and Language are format-supported on
        // the wire but silently dropped by ffmpeg's MP4 muxer, so they live in the same bucket.
        target.Subtitle = source.Subtitle;
        target.Isbn = source.Isbn;
        target.Identifiers = source.Identifiers;
        target.Source = source.Source;
        target.Type = source.Type;
        target.Relation = source.Relation;
        target.Coverage = source.Coverage;
        target.Format = source.Format;
        target.Publisher = source.Publisher;
        target.Language = source.Language;
    }

    public string? ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid) => m4BFileHandler.ExtractCover(filePath, coverOutputDirectory, coverUuid).GetAwaiter().GetResult();
}
