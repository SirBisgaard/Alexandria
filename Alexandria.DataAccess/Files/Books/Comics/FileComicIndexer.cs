using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Files.Books.Comics;

/// <summary>
/// Indexes CBZ comics in <see cref="AlexandriaSettings.EbookFolder"/>. CBR files never reach this
/// indexer — <see cref="FileCbrComicConverter"/> repacks them as CBZ earlier in the same run.
/// </summary>
public class FileComicIndexer(AlexandriaSettings settings, ICbzFileHandler cbzFileHandler, IComicEpubBuilder epubBuilder, IComicRenditionStore renditionStore, IFileRepository fileRepository, ICoverThumbnailService coverThumbnailService, ISettingsService settingsService, IProgressService progressService, ILogger<FileComicIndexer> logger) : IFileContentIndexer
{
    public bool CanHandle(string path) => path.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase);

    public string[] GetContentFiles()
    {
        if (!Directory.Exists(settings.EbookFolder))
            return [];

        return Directory.GetFiles(settings.EbookFolder, "*.cbz")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<(Book Metadata, byte[]? CoverBytes)> Extract(string filePath)
    {
        var (metadata, rawCover) = cbzFileHandler.Extract(filePath);
        var cover = rawCover is null ? null : IsJpeg(rawCover) ? rawCover : coverThumbnailService.ConvertToJpeg(rawCover);
        return Task.FromResult((metadata, cover));
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
    /// Builds a fixed-layout EPUB for every screen profile among the registered devices, skipping
    /// profiles whose current rendition already exists. Older renditions of the same book in that
    /// profile (previous direction, colour mode or source revision) are removed first. A failed
    /// build is logged and skipped so one broken archive never aborts the library sync; the
    /// device simply doesn't receive that comic until a later sync succeeds.
    /// TotalFileSize reports the CBZ source size — the size each device downloads comes from its
    /// own rendition at sync time.
    /// </summary>
    public async Task CreateDerivativeFiles(Book book, string sourcePath)
    {
        book.File.TotalFileSize = fileRepository.GetFileSize(sourcePath);
        var rendering = (await settingsService.Get()).ComicRendering;

        foreach (var profile in await renditionStore.GetActiveProfiles())
        {
            var renditionPath = await renditionStore.GetRenditionPath(book, profile);
            if (File.Exists(renditionPath))
                continue;

            try
            {
                RemoveOtherRenditions(book.BookUuid, renditionPath);
                var subTask = $"Building {profile.Key} comic";
                await Task.Run(() => epubBuilder.Build(book, profile, rendering, renditionPath, (done, total) => progressService.ReportSubTask(subTask, done, total)));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to build {Profile} rendition of '{Title}' ({SourcePath})", profile.Key, book.Title, sourcePath);
            }
        }
    }

    public void DeleteDerivedFiles(Book book)
    {
        fileRepository.DeleteCover(book);
        renditionStore.DeleteRenditions(book.BookUuid);
    }

    /// <summary>
    /// Removes renditions of books that no longer exist and whole profile folders no registered
    /// device uses any more (e.g. after a device was deleted).
    /// </summary>
    public void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths)
    {
        if (!Directory.Exists(settings.ComicsFolder))
            return;

        var activeKeys = renditionStore.GetActiveProfiles().GetAwaiter().GetResult().Select(p => p.Key).ToHashSet();
        foreach (var profileFolder in Directory.GetDirectories(settings.ComicsFolder))
        {
            if (!activeKeys.Contains(Path.GetFileName(profileFolder)))
            {
                logger.LogInformation("Removing comic renditions for unused device profile {Profile}", Path.GetFileName(profileFolder));
                Directory.Delete(profileFolder, recursive: true);
                continue;
            }

            foreach (var file in Directory.GetFiles(profileFolder))
            {
                var name = Path.GetFileName(file);
                var dot = name.IndexOf('.');
                if (dot > 0 && Guid.TryParse(name[..dot], out var uuid) && !validBookUuids.Contains(uuid))
                    File.Delete(file);
            }
        }
    }

    private static void RemoveOtherRenditions(Guid bookUuid, string currentPath)
    {
        var folder = Path.GetDirectoryName(currentPath)!;
        if (!Directory.Exists(folder))
            return;
        foreach (var file in Directory.GetFiles(folder, $"{bookUuid}.*"))
        {
            if (!string.Equals(file, currentPath, StringComparison.Ordinal))
                File.Delete(file);
        }
    }

    private static bool IsJpeg(byte[] data) => data is [0xFF, 0xD8, ..];
}
