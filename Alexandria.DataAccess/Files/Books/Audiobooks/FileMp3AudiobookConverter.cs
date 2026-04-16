using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
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
public class FileMp3AudiobookConverter(AlexandriaSettings settings, ISettingsService settingsService, IM4BFileHandler m4BFileHandler, IFfmpegWrapper ffmpegWrapper, ICoverThumbnailService coverThumbnailService, ILogger<FileMp3AudiobookConverter> logger) : IFileConverter
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

        foreach (var sourcePath in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = booksByPath.GetValueOrDefault(sourcePath);
            await ConvertOne(sourcePath, existing, cancellationToken);
        }
    }

    private async Task ConvertOne(string sourcePath, Book? existing, CancellationToken cancellationToken)
    {
        var tempCopy = sourcePath + ".temp";
        string? targetDir = null;

        try
        {
            var extracted = await m4BFileHandler.ExtractMetadata(sourcePath);
            var coverBytes = await m4BFileHandler.ExtractCoverBytes(sourcePath);

            // Identity metadata (title, authors, series, ...) comes from the DB when available
            // so the re-indexed MP3 folder produces the same UUID and carries any UI edits.
            // Chapter timings and audio codec info only live in the file — keep those from the extraction.
            var canonical = existing ?? extracted;

            var folderName = BuildFolderName(canonical);
            var candidateDir = Path.Combine(settings.AudiobookFolder, folderName);

            if (Directory.Exists(candidateDir))
            {
                logger.LogWarning("Target folder already exists, skipping {Source} → {Target}", sourcePath, candidateDir);
                return;
            }

            File.Copy(sourcePath, tempCopy, overwrite: true);
            Directory.CreateDirectory(candidateDir);
            targetDir = candidateDir;

            await TranscodeChapters(extracted, tempCopy, targetDir, cancellationToken);
            var jpegCover = await WriteCover(targetDir, coverBytes);
            await EmbedChapterTags(canonical, extracted.Chapters, targetDir, jpegCover);

            File.Delete(sourcePath);
            File.Delete(tempCopy);
            CleanupCachedChapters(existing);
            logger.LogInformation("Converted {Source} → {Target}", sourcePath, targetDir);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert {Source}; leaving original intact", sourcePath);
            CleanupTarget(targetDir);
            CleanupTemp(tempCopy);
        }
    }

    private async Task TranscodeChapters(Book extracted, string inputPath, string targetDir, CancellationToken cancellationToken)
    {
        var total = extracted.Chapters.Count;
        var padWidth = total.ToString().Length;
        var completed = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(Enumerable.Range(0, total), options, async (i, ct) =>
        {
            var chapter = extracted.Chapters[i];
            var number = (i + 1).ToString().PadLeft(padWidth, '0');
            var outPath = Path.Combine(targetDir, $"{number} - {SanitizeFsName(chapter.Title)}.mp3");
            await ffmpegWrapper.TranscodeChapterToFile(inputPath, chapter.StartTime, chapter.EndTime, extracted.File.AudioCodec, extracted.File.AudioBitrate, outPath, ct);
            var done = Interlocked.Increment(ref completed);
            logger.LogInformation("Converted chapter {Completed} out of {Total} for {Title}", done, total, extracted.Title);
        });
    }

    private async Task<byte[]?> WriteCover(string targetDir, byte[]? coverBytes)
    {
        if (coverBytes is null)
            return null;
        var bytes = IsJpeg(coverBytes) ? coverBytes : coverThumbnailService.ConvertToJpeg(coverBytes);
        await File.WriteAllBytesAsync(Path.Combine(targetDir, "cover.jpg"), bytes);
        return bytes;
    }

    private async Task EmbedChapterTags(Book canonical, IReadOnlyList<AudiobookChapter> chapters, string targetDir, byte[]? coverBytes)
    {
        var files = Directory.GetFiles(targetDir, "*.mp3")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var total = files.Length;
        var albumTags = BuildAlbumTags(canonical);

        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var tags = new Dictionary<string, string>(albumTags)
            {
                ["title"] = i < chapters.Count ? chapters[i].Title : Path.GetFileNameWithoutExtension(file),
                ["track"] = $"{i + 1}/{total}"
            };
            var tempPath = file + ".tmp";
            try
            {
                if (coverBytes is null)
                    await ffmpegWrapper.WriteMetadata(file, tempPath, tags, []);
                else
                    await ffmpegWrapper.WriteMetadataWithCover(file, tempPath, tags, [], coverBytes);
                File.Move(tempPath, file, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
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

    private void CleanupTemp(string tempCopy)
    {
        if (!File.Exists(tempCopy))
            return;
        try
        {
            File.Delete(tempCopy);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up temp file {Temp}", tempCopy);
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

    private static Dictionary<string, string> BuildAlbumTags(Book book)
    {
        var tags = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(book.Title))
            tags["album"] = book.Subtitle is not null ? $"{book.Title}: {book.Subtitle}" : book.Title;

        if (book.Authors.Count > 0)
        {
            var authorStr = string.Join(", ", book.Authors.Select(a => a.Name));
            tags["artist"] = authorStr;
            tags["album_artist"] = authorStr;
        }

        var narrators = book.Contributors.Where(c => c.Role == "Narrator").ToList();
        if (narrators.Count > 0)
            tags["composer"] = string.Join(", ", narrators.Select(n => n.Name));

        if (book.Description is not null)
        {
            tags["comment"] = book.Description;
            tags["description"] = book.Description;
        }

        if (book.PublicationDate is not null)
            tags["date"] = book.PublicationDate;

        if (book.Rights is not null)
            tags["copyright"] = book.Rights;

        if (book.Publisher is not null)
            tags["publisher"] = book.Publisher;

        if (book.Subjects.Count > 0)
            tags["genre"] = string.Join("; ", book.Subjects);

        if (book.Series is not null)
        {
            var grouping = book.Series.Name;
            if (book.Series.Index is not null)
                grouping += $" #{book.Series.Index.Value}";
            tags["grouping"] = grouping;
        }

        return tags;
    }

    private static bool IsJpeg(byte[] data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;
}
