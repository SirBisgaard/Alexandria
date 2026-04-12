using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileAudiobookIndexer(AlexandriaSettings settings, FileAudiobookMediaHandler mediaHandler, ICoverThumbnailService coverThumbnailService) : IFileContentIndexer
{
    public string[] SupportedExtensions => [".m4b"];

    public string[] GetContentFiles()
    {
        if (!Directory.Exists(settings.AudiobookFolder))
            return [];

        return Directory.GetFiles(settings.AudiobookFolder, "*.m4b");
    }

    public Book ExtractMetadata(string filePath) => mediaHandler.ExtractMetadata(filePath);

    public byte[]? ExtractCoverBytes(string filePath)
    {
        var raw = mediaHandler.ExtractCoverBytes(filePath);
        if (raw is null)
            return null;
        return IsJpeg(raw) ? raw : coverThumbnailService.ConvertToJpeg(raw);
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

    public Task CreateDerivativeFiles(Book book, string sourcePath)
    {
        return Task.CompletedTask;
    }

    public void DeleteDerivedFiles(Book book)
    {
        if (book.CoverFilePath is not null && File.Exists(book.CoverFilePath))
            File.Delete(book.CoverFilePath);
    }

    public void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths)
    {
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
