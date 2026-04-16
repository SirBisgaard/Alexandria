using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileMp3AudiobookIndexer(AlexandriaSettings settings, FileMp3AudiobookMediaHandler mediaHandler, ICoverThumbnailService coverThumbnailService) : IFileContentIndexer
{
    public string[] SupportedExtensions => [".mp3audiobook"];

    public string[] GetContentFiles()
    {
        if (!Directory.Exists(settings.AudiobookFolder))
            return [];

        return Directory.GetDirectories(settings.AudiobookFolder)
            .Where(dir => Directory.GetFiles(dir, "*.mp3").Length > 0)
            .Select(dir => dir + ".mp3audiobook")
            .ToArray();
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
        if (book.File.CoverFilePath is not null && File.Exists(book.File.CoverFilePath))
            File.Delete(book.File.CoverFilePath);
    }

    public void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths)
    {
    }

    private static bool IsJpeg(byte[] data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;
}
