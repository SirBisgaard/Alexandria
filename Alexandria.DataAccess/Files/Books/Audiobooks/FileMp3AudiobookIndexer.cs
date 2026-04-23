using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileMp3AudiobookIndexer(AlexandriaSettings settings, ICoverThumbnailService coverThumbnailService, IMp3FileHandler mp3FileHandler) : IFileContentIndexer
{
    public bool CanHandle(string path)
    {
        if (!Directory.Exists(path))
            return false;
        if (string.Equals(Path.GetFileName(path), FileMp3AudiobookConverter.FailedFolderName, StringComparison.OrdinalIgnoreCase))
            return false;
        return Directory.EnumerateFiles(path, "*.mp3").Any();
    }

    public string[] GetContentFiles()
    {
        if (!Directory.Exists(settings.AudiobookFolder))
            return [];

        return Directory.GetDirectories(settings.AudiobookFolder)
            .Where(CanHandle)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<(Book Metadata, byte[]? CoverBytes)> Extract(string filePath)
    {
        var (metadata, rawCover) = await mp3FileHandler.Extract(filePath);
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

    private static bool IsJpeg(byte[] data) => data is [0xFF, 0xD8, ..];
}
