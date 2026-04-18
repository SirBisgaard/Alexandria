using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileM4BAudiobookIndexer(AlexandriaSettings settings, ICoverThumbnailService coverThumbnailService, ISettingsService settingsService, IM4BFileHandler m4BFileHandler, IIndexingProgressService progressService, ILogger<FileM4BAudiobookIndexer> logger) : IFileContentIndexer
{
    public string[] SupportedExtensions => [".m4b"];

    public string[] GetContentFiles()
    {
        if (!Directory.Exists(settings.AudiobookFolder))
            return [];

        return Directory.GetFiles(settings.AudiobookFolder, "*.m4b");
    }

    public async Task<(Book Metadata, byte[]? CoverBytes)> Extract(string filePath)
    {
        var metadata = await m4BFileHandler.ExtractMetadata(filePath);
        var rawCover = await m4BFileHandler.ExtractCoverBytes(filePath);
        var cover = rawCover is null ? null : IsJpeg(rawCover) ? rawCover : coverThumbnailService.ConvertToJpeg(rawCover);
        return (metadata, cover);
    }

    public string? SaveCover(byte[]? coverBytes, Guid bookUuid)
    {
        if (coverBytes is null)
            return null;
        Directory.CreateDirectory(settings.CoversFolder);
        var bytes = IsJpeg(coverBytes) ? coverBytes : coverThumbnailService.ConvertToJpeg(coverBytes);
        var path = Path.Combine(settings.CoversFolder, $"{bookUuid}.jpg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// When <see cref="M4BFileTreatment.TranscodeAtIndex"/> is selected, pre-transcodes each chapter
    /// to an MP3 file under <see cref="AlexandriaSettings.ChaptersFolder"/>/{bookUuid}/{chapterIndex}.mp3.
    /// Per-chapter existence check makes re-runs a no-op (same pattern as the KEPUB derivative).
    /// </summary>
    public async Task CreateDerivativeFiles(Book book, string sourcePath)
    {
        var appSettings = await settingsService.Get();
        if (appSettings.M4BFileTreatment != M4BFileTreatment.TranscodeAtIndex)
            return;

        var bookFolder = Path.Combine(settings.ChaptersFolder, book.BookUuid.ToString());
        Directory.CreateDirectory(bookFolder);

        var total = book.Chapters.Count;
        var completed = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismPolicy.MaxDegree };
        await Parallel.ForEachAsync(Enumerable.Range(0, total), options, async (i, ct) =>
        {
            var outputPath = Path.Combine(bookFolder, $"{i}.mp3");
            if (!File.Exists(outputPath))
                await m4BFileHandler.TranscodeChapterToFile(book, i, outputPath, ct);
            var done = Interlocked.Increment(ref completed);
            logger.LogInformation("Pre-transcoded {Completed} out of {Total} chapters for {BookTitle}", done, total, book.Title);
            progressService.ReportSubTask("Transcoding chapters", done, total);
        });
    }

    public void DeleteDerivedFiles(Book book)
    {
        if (book.File.CoverFilePath is not null && File.Exists(book.File.CoverFilePath))
            File.Delete(book.File.CoverFilePath);

        var chapterFolder = Path.Combine(settings.ChaptersFolder, book.BookUuid.ToString());
        if (Directory.Exists(chapterFolder))
            Directory.Delete(chapterFolder, recursive: true);
    }

    public void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths)
    {
        if (!Directory.Exists(settings.ChaptersFolder))
            return;

        foreach (var dir in Directory.GetDirectories(settings.ChaptersFolder))
        {
            var name = Path.GetFileName(dir);
            if (Guid.TryParse(name, out var uuid) && !validBookUuids.Contains(uuid))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static bool IsJpeg(byte[] data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    private string? ConvertCoverToJpeg(string? coverFilePath)
    {
        if (coverFilePath is null)
            return null;

        var extension = Path.GetExtension(coverFilePath);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return coverFilePath;

        var jpegBytes = coverThumbnailService.ConvertToJpeg(File.ReadAllBytes(coverFilePath));
        var jpegPath = Path.ChangeExtension(coverFilePath, ".jpg");
        File.WriteAllBytes(jpegPath, jpegBytes);
        File.Delete(coverFilePath);
        return jpegPath;
    }
}
