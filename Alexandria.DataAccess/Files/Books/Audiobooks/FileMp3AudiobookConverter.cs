using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

/// <summary>
/// Pre-index converter for <see cref="M4BFileTreatment.ConvertAtIndex"/>. Turns each
/// <c>.m4b</c> in <see cref="AlexandriaSettings.AudiobookFolder"/> into an MP3 folder
/// ("{Author} - {Series} #{Index} - {Title}"), then deletes the original M4B. The
/// resulting folder is indexed by <see cref="FileMp3AudiobookIndexer"/> on the same run.
/// </summary>
public class FileMp3AudiobookConverter(AlexandriaSettings settings, ISettingsService settingsService, IM4BFileHandler m4BFileHandler, IFfmpegWrapper ffmpegWrapper, ICoverThumbnailService coverThumbnailService, IIndexingProgressService progressService, ILogger<FileMp3AudiobookConverter> logger) : IFileConverter
{
    public async Task Convert(IReadOnlyList<Book> existingBooks, CancellationToken cancellationToken)
    {
        var appSettings = await settingsService.Get();
        if (appSettings.M4BFileTreatment != M4BFileTreatment.ConvertAtIndex)
            return;

        if (!Directory.Exists(settings.AudiobookFolder))
            return;

        var sourceFiles = Directory.GetFiles(settings.AudiobookFolder, "*.m4b");
        if (sourceFiles.Length == 0)
            return;

        logger.LogInformation("ConvertAtIndex: found {Count} M4B file(s) to convert", sourceFiles.Length);

        var booksByPath = existingBooks
            .GroupBy(b => b.File.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sourceFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = booksByPath.GetValueOrDefault(sourceFiles[i]);
            await ConvertOne(sourceFiles[i], existing, i + 1, sourceFiles.Length, cancellationToken);
        }
    }

    private async Task ConvertOne(string sourcePath, Book? existing, int bookNumber, int totalBooks, CancellationToken cancellationToken)
    {
        string? targetDir = null;

        try
        {
            var extracted = await m4BFileHandler.ExtractMetadata(sourcePath);
            var coverBytes = await m4BFileHandler.ExtractCoverBytes(sourcePath);

            progressService.ReportConverting(extracted.Title, bookNumber, totalBooks);

            // Identity metadata (title, authors, series, ...) comes from the DB when available
            // so the re-indexed MP3 folder produces the same UUID and carries any UI edits.
            // Chapter timings and titles only live in the file — overlay those from the extraction.
            var canonical = existing ?? extracted;
            canonical.Chapters = extracted.Chapters;

            var folderName = BuildFolderName(canonical);
            var candidateDir = Path.Combine(settings.AudiobookFolder, folderName);

            if (Directory.Exists(candidateDir))
            {
                logger.LogWarning("Target folder already exists, skipping {Source} → {Target}", sourcePath, candidateDir);
                return;
            }

            Directory.CreateDirectory(candidateDir);
            targetDir = candidateDir;

            // Keep the working copy of the source M4B inside the target folder so that a failed
            // conversion cleans itself up entirely when CleanupTarget deletes the folder recursively.
            var tempCopy = Path.Combine(targetDir, ".source.m4b");
            File.Copy(sourcePath, tempCopy, overwrite: true);

            // Normalize + write cover.jpg upfront so every chapter's ffmpeg invocation can
            // point at one on-disk file instead of piping bytes in N times.
            string? coverJpgPath = null;
            if (coverBytes is not null)
            {
                var jpeg = IsJpeg(coverBytes) ? coverBytes : coverThumbnailService.ConvertToJpeg(coverBytes);
                coverJpgPath = Path.Combine(targetDir, "cover.jpg");
                await File.WriteAllBytesAsync(coverJpgPath, jpeg, cancellationToken);
            }

            await TranscodeChapters(canonical, extracted, tempCopy, targetDir, coverJpgPath, cancellationToken);

            File.Delete(tempCopy);
            File.Delete(sourcePath);
            CleanupCachedChapters(existing);
            logger.LogInformation("Converted {Source} → {Target}", sourcePath, targetDir);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert {Source}; leaving original intact", sourcePath);
            CleanupTarget(targetDir);
        }
    }

    private async Task TranscodeChapters(Book canonical, Book extracted, string inputPath, string targetDir, string? coverJpgPath, CancellationToken cancellationToken)
    {
        var total = extracted.Chapters.Count;
        var padWidth = total.ToString().Length;
        var completed = 0;
        var albumTags = Mp3FileHandler.BuildStandardTagDictionary(canonical);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(Enumerable.Range(0, total), options, async (i, ct) =>
        {
            var chapter = extracted.Chapters[i];
            var number = (i + 1).ToString().PadLeft(padWidth, '0');
            var outPath = Path.Combine(targetDir, $"{number} - {SanitizeFsName(chapter.Title)}.mp3");
            var perFileTags = new Dictionary<string, string>(albumTags)
            {
                ["title"] = chapter.Title,
                ["track"] = $"{i + 1}/{total}"
            };
            var qaLevel = extracted.File.AudioCodec == "mp3" ? (int?)null : FfmpegWrapper.SelectLameQualityForConversion(extracted.File.AudioBitrate);
            await ffmpegWrapper.TranscodeChapterToFile(inputPath, chapter.StartTime, chapter.EndTime, qaLevel, outPath, perFileTags, coverJpgPath, ct);
            var done = Interlocked.Increment(ref completed);
            logger.LogInformation("Converted chapter {Completed} out of {Total} for {Title}", done, total, extracted.Title);
            progressService.ReportSubTask("Transcoding chapters", done, total);
        });
    }

    private void CleanupCachedChapters(Book? existing)
    {
        if (existing is null)
            return;
        var cachedDir = Path.Combine(settings.ChaptersFolder, existing.BookUuid.ToString());
        if (!Directory.Exists(cachedDir))
            return;
        try
        {
            Directory.Delete(cachedDir, recursive: true);
            logger.LogInformation("Removed stale chapter cache for {Title} at {Dir}", existing.Title, cachedDir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove stale chapter cache {Dir}", cachedDir);
        }
    }

    private void CleanupTarget(string? targetDir)
    {
        if (targetDir is null || !Directory.Exists(targetDir))
            return;
        try
        {
            Directory.Delete(targetDir, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up target folder {Target}", targetDir);
        }
    }

    private static string BuildFolderName(Book book)
    {
        var author = book.Authors.Count > 0 ? string.Join(", ", book.Authors.Select(a => a.Name)) : "Unknown Author";
        string? series = null;
        if (book.Series is not null)
            series = book.Series.Index is not null ? $"{book.Series.Name} #{book.Series.Index.Value}" : book.Series.Name;

        var raw = series is not null ? $"{author} - {series} - {book.Title}" : $"{author} - {book.Title}";
        return SanitizeFsName(raw);
    }

    private static string SanitizeFsName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");
        return cleaned;
    }

    private static bool IsJpeg(byte[] data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;
}
