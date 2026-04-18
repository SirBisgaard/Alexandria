using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Ebooks;

public class FileEbookIndexer(AlexandriaSettings settings, IFileRepository fileRepository, IEpubFileHandler epubFileHandler, IKepubifyWrapper kepubifyWrapper, ICoverThumbnailService coverThumbnailService) : IFileContentIndexer
{
    public string[] SupportedExtensions => [".epub"];

    public string[] GetContentFiles() => fileRepository.GetEpubFiles();

    public Task<(Book Metadata, byte[]? CoverBytes)> Extract(string filePath)
    {
        var (metadata, rawCover) = epubFileHandler.Extract(filePath);
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
    /// Creates a KEPUB derivative from the source EPUB via kepubify. Created once at index time
    /// and reused — existence check avoids expensive re-conversion on subsequent runs.
    /// TotalFileSize is reported from the KEPUB derivative (not source EPUB) because the device
    /// uses this for download progress and storage estimates.
    /// </summary>
    public async Task CreateDerivativeFiles(Book book, string sourcePath)
    {
        var kepubPath = Path.Combine(settings.KepubsFolder, $"{book.BookUuid}.kepub.epub");
        if (!File.Exists(kepubPath))
            await kepubifyWrapper.Convert(sourcePath, kepubPath);
        book.File.KepubFilePath = kepubPath;
        book.File.TotalFileSize = fileRepository.GetFileSize(kepubPath);
    }

    public void DeleteDerivedFiles(Book book)
    {
        fileRepository.DeleteCover(book);
        fileRepository.DeleteKepub(book);
    }

    public void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths)
    {
        if (Directory.Exists(settings.KepubsFolder))
        {
            foreach (var file in Directory.GetFiles(settings.KepubsFolder, "*.kepub.epub"))
            {
                var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
                if (Guid.TryParse(name, out var uuid) && !validBookUuids.Contains(uuid))
                    File.Delete(file);
            }
        }

        if (Directory.Exists(settings.CoversFolder))
        {
            foreach (var file in Directory.GetFiles(settings.CoversFolder))
            {
                if (!validCoverPaths.Contains(file))
                    File.Delete(file);
            }
        }
    }

    /// <summary>
    /// Detects JPEG by SOI magic bytes (0xFF 0xD8). Non-JPEG covers (PNG, GIF) are converted
    /// to JPEG because Kobo devices display JPEG covers most reliably.
    /// </summary>
    private static bool IsJpeg(byte[] data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;
}
