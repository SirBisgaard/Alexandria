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
    private const string SentinelFileName = ".converting";
    public const string FailedFolderName = "alexandria-conversion-failed";

    public async Task Convert(IReadOnlyList<Book> existingBooks, CancellationToken cancellationToken)
    {
        var appSettings = await settingsService.Get();
        if (appSettings.M4BFileTreatment != M4BFileTreatment.ConvertAtIndex)
            return;

        if (!Directory.Exists(settings.AudiobookFolder))
            return;

        var m4BPaths = Directory.GetFiles(settings.AudiobookFolder, "*.m4b")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        logger.LogInformation("ConvertAtIndex: found {Count} M4B file(s) to convert", m4BPaths.Length);
        if (m4BPaths.Length == 0)
            return;

        var existingByPath = existingBooks
            .Where(b => Path.GetExtension(b.File.SourcePath).Equals(".m4b", StringComparison.OrdinalIgnoreCase))
            .GroupBy(b => b.File.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var claimedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < m4BPaths.Length; i++)
        {
            var path = m4BPaths[i];
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var extracted = await m4BFileHandler.ExtractMetadata(path);
                var existing = existingByPath.GetValueOrDefault(path);
                var canonical = existing ?? extracted;
                canonical.Chapters = extracted.Chapters;

                var candidateDir = Path.Combine(settings.AudiobookFolder, BuildFolderName(canonical));

                if (!claimedTargets.Add(candidateDir))
                {
                    logger.LogWarning("Skipping {Source}: folder '{Dir}' already claimed by another source this run", path, candidateDir);
                    continue;
                }

                if (Directory.Exists(candidateDir))
                {
                    if (IsStaleTempDir(candidateDir))
                    {
                        logger.LogWarning("Recovering stale temp folder {Dir} before converting {Source}", candidateDir, path);
                        Directory.Delete(candidateDir, recursive: true);
                    }
                    else
                    {
                        logger.LogWarning("Target folder {Dir} already converted.", candidateDir);
                        continue;
                    }
                }

                await ConvertOne(path, candidateDir, canonical, extracted, existing, i + 1, m4BPaths.Length, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to convert {Source}; quarantining source file", path);
                QuarantineFailedSource(path);
            }
        }
    }

    private async Task ConvertOne(string sourcePath, string targetDir, Book canonical, Book extracted, Book? existing, int bookNumber, int totalBooks, CancellationToken cancellationToken)
    {
        try
        {
            progressService.ReportConverting(extracted.Title, bookNumber, totalBooks);

            var coverBytes = await m4BFileHandler.ExtractCoverBytes(sourcePath);

            Directory.CreateDirectory(targetDir);

            // Sentinel is created before anything else touches the folder so that a crash at any
            // point during conversion leaves a recoverable marker. Removed below just before the
            // source M4B is deleted, so a completed folder never carries the sentinel.
            var sentinelPath = Path.Combine(targetDir, SentinelFileName);
            File.Create(sentinelPath).Dispose();

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
            File.Delete(sentinelPath);
            File.Delete(sourcePath);
            CleanupCachedChapters(existing);
            logger.LogInformation("Converted {Source} → {Target}", sourcePath, targetDir);
        }
        catch
        {
            CleanupTarget(targetDir);
            throw;
        }
    }

    private async Task TranscodeChapters(Book canonical, Book extracted, string inputPath, string targetDir, string? coverJpgPath, CancellationToken cancellationToken)
    {
        var total = extracted.Chapters.Count;
        var padWidth = total.ToString().Length;
        var completed = 0;
        var albumTags = Mp3FileHandler.BuildStandardTagDictionary(canonical);
        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismPolicy.MaxDegree, CancellationToken = cancellationToken };

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
            logger.LogError(ex, "Failed to remove stale chapter cache {Dir}", cachedDir);
        }
    }

    /// <summary>
    /// Moves a source M4B that failed conversion into the <see cref="FailedFolderName"/>
    /// quarantine folder so the next indexing run does not re-attempt and re-fail it.
    /// Indexers do not recurse into subfolders of <see cref="AlexandriaSettings.AudiobookFolder"/>,
    /// so quarantined files are excluded from discovery. Overwrites any prior copy at the
    /// destination — a retry of the same source simply replaces its previous failed copy.
    /// </summary>
    private void QuarantineFailedSource(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return;
        try
        {
            var failedDir = Path.Combine(settings.AudiobookFolder, FailedFolderName);
            Directory.CreateDirectory(failedDir);
            var destPath = Path.Combine(failedDir, Path.GetFileName(sourcePath));
            File.Move(sourcePath, destPath, overwrite: true);
            logger.LogWarning("Quarantined failed source {Source} -> {Dest}", sourcePath, destPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to quarantine source {Source}", sourcePath);
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
            logger.LogError(ex, "Failed to clean up target folder {Target}", targetDir);
        }
    }

    /// <summary>
    /// A target directory is "stale temp" — safe to recursively delete and retry — when it
    /// either carries the <c>.converting</c> sentinel from an interrupted prior attempt, or
    /// is completely empty (sentinel creation may itself have failed, leaving an empty dir).
    /// Anything else is treated as an already-converted folder and left alone.
    /// </summary>
    private static bool IsStaleTempDir(string dir)
    {
        if (File.Exists(Path.Combine(dir, SentinelFileName)))
            return true;
        return Directory.GetFileSystemEntries(dir).Length == 0;
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

    /// <summary>
    /// Replaces characters that are invalid either on the local filesystem, when the filename
    /// is embedded in <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/> (notably <c>"</c>,
    /// which .NET's CRT-style parser silently strips), or on FAT32/exFAT storage like the Kobo
    /// device that these files are ultimately copied to. Uses the union of
    /// <see cref="Path.GetInvalidFileNameChars"/> (host-specific; on Linux only <c>\0</c> and
    /// <c>/</c>) and a hardcoded cross-platform + argv-safe set, so the host-specific list stays
    /// as a safety net behind the explicit one.
    /// </summary>
    private static string SanitizeFsName(string input)
    {
        var cleaned = new string(input.Select(c => InvalidFsNameChars.Contains(c) ? ' ' : c).ToArray()).Trim();
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");
        return cleaned;
    }

    private static readonly HashSet<char> InvalidFsNameChars = BuildInvalidFsNameChars();

    private static HashSet<char> BuildInvalidFsNameChars()
    {
        var set = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (var c in "<>:\"/\\|?*")
            set.Add(c);
        for (var c = (char)0; c < 0x20; c++)
            set.Add(c);
        return set;
    }

    private static bool IsJpeg(byte[] data) => data is [0xFF, 0xD8, ..];
}
