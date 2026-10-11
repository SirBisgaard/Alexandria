using System.IO.Compression;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Alexandria.DataAccess.Files.Books.Comics;

/// <summary>
/// Pre-index converter that repacks every <c>.cbr</c> in <see cref="AlexandriaSettings.EbookFolder"/>
/// as a <c>.cbz</c> with the same base name, then deletes the original. RAR archives cannot be
/// written, so converting is what makes comic metadata editable (ComicInfo.xml inside the zip).
/// Solid archives are streamed through SharpCompress's forward-only reader, which decompresses
/// them in a single pass instead of restarting for every entry; non-solid archives (which the
/// forward-only reader rejects) are read entry by entry. Page images are stored
/// uncompressed — they are already JPEG/PNG. A failed conversion moves the CBR into
/// <see cref="FailedFolderName"/> so it is not retried on every sync.
/// </summary>
public class FileCbrComicConverter(AlexandriaSettings settings, IProgressService progressService, ILogger<FileCbrComicConverter> logger) : IFileConverter
{
    public const string FailedFolderName = "alexandria-conversion-failed";

    public Task Convert(IReadOnlyList<Book> existingBooks, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(settings.EbookFolder))
            return Task.CompletedTask;

        var cbrPaths = Directory.GetFiles(settings.EbookFolder, "*.cbr")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (cbrPaths.Length == 0)
            return Task.CompletedTask;

        logger.LogInformation("Converting {Count} CBR file(s) to CBZ", cbrPaths.Length);
        for (var i = 0; i < cbrPaths.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cbrPath = cbrPaths[i];
            var cbzPath = Path.ChangeExtension(cbrPath, ".cbz");
            progressService.Report("Converting comics", i + 1, cbrPaths.Length);

            if (File.Exists(cbzPath))
            {
                logger.LogWarning("Skipping {Source}: {Target} already exists", cbrPath, cbzPath);
                continue;
            }

            try
            {
                ConvertOne(cbrPath, cbzPath, cancellationToken);
                File.Delete(cbrPath);
                logger.LogInformation("Converted {Source} → {Target}", cbrPath, cbzPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to convert {Source}; quarantining source file", cbrPath);
                QuarantineFailedSource(cbrPath);
            }
        }

        return Task.CompletedTask;
    }

    private static void ConvertOne(string cbrPath, string cbzPath, CancellationToken cancellationToken)
    {
        var tempPath = cbzPath + ".converting";
        try
        {
            int expectedFiles;
            var writtenFiles = 0;
            using (var source = new FileStream(cbrPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = ArchiveFactory.OpenArchive(source, new ReaderOptions()))
            using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(target, ZipArchiveMode.Create))
            {
                expectedFiles = archive.Entries.Count(e => !e.IsDirectory);
                if (archive.IsSolid)
                {
                    using var reader = archive.ExtractAllEntries();
                    while (reader.MoveToNextEntry())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (TryCreateZipEntry(zip, reader.Entry) is not { } zipEntry)
                            continue;
                        using var output = zipEntry.Open();
                        reader.WriteEntryTo(output);
                        writtenFiles++;
                    }
                }
                else
                {
                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (TryCreateZipEntry(zip, entry) is not { } zipEntry)
                            continue;
                        using var input = entry.OpenEntryStream();
                        using var output = zipEntry.Open();
                        input.CopyTo(output);
                        writtenFiles++;
                    }
                }
            }

            if (writtenFiles != expectedFiles)
                throw new InvalidDataException($"Wrote {writtenFiles} of {expectedFiles} entries from '{cbrPath}'.");

            File.Move(tempPath, cbzPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static ZipArchiveEntry? TryCreateZipEntry(ZipArchive zip, IEntry entry)
    {
        if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
            return null;

        var name = CbzFileHandler.NormalizePath(entry.Key);
        var compression = CbzFileHandler.IsImage(name) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
        var zipEntry = zip.CreateEntry(name, compression);
        if (entry.LastModifiedTime is { } modified)
            zipEntry.LastWriteTime = modified;
        return zipEntry;
    }

    private void QuarantineFailedSource(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return;
        try
        {
            var failedDir = Path.Combine(settings.EbookFolder, FailedFolderName);
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
}
