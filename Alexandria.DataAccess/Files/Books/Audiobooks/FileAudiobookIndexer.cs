using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Audiobooks;

public class FileAudiobookIndexer(AlexandriaSettings settings, FileAudiobookMediaHandler mediaHandler, IFileRepository fileRepository, ICoverThumbnailService coverThumbnailService) : IFileContentIndexer
{
    public string[] SupportedExtensions => [".m4b"];

    public string[] GetContentFiles()
    {
        if (!Directory.Exists(settings.AudiobookFolder))
            return [];

        return Directory.GetFiles(settings.AudiobookFolder, "*.m4b");
    }

    public Task<string> ComputeFileHash(string filePath) => fileRepository.ComputeFileHash(filePath);

    public (Book Book, string? CoverFilePath) ExtractAll(string filePath)
    {
        var book = mediaHandler.ExtractMetadata(filePath);
        var coverUuid = book.BookUuid != Guid.Empty ? book.BookUuid : book.GenerateDeterministicUuid();
        Directory.CreateDirectory(settings.CoversFolder);
        var coverFilePath = mediaHandler.ExtractCover(filePath, settings.CoversFolder, coverUuid);
        coverFilePath = ConvertCoverToJpeg(coverFilePath);
        return (book, coverFilePath);
    }

    public void StampIdentifier(string filePath, Book book) => mediaHandler.EmbedMetadata(filePath, book);

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
