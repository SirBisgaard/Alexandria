using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Books.Ebooks;

public class FileEbookIndexer(AlexandriaSettings settings, IFileRepository fileRepository, IEpubMetadataHandler epubMetadataHandler, IKepubifyConverter kepubifyConverter, ICoverThumbnailService coverThumbnailService) : IFileContentIndexer
{
    public string[] SupportedExtensions => [".epub"];

    public string[] GetContentFiles() => fileRepository.GetEpubFiles();

    public Task<string> ComputeFileHash(string filePath) => fileRepository.ComputeFileHash(filePath);

    public (Book Book, string? CoverFilePath) ExtractAll(string filePath)
    {
        var (book, coverFilePath) = epubMetadataHandler.ExtractAll(filePath, settings.CoversFolder);
        coverFilePath = ConvertCoverToJpeg(coverFilePath);
        return (book, coverFilePath);
    }

    public async Task CreateDerivativeFiles(Book book, string sourcePath)
    {
        var kepubPath = Path.Combine(settings.KepubsFolder, $"{book.BookUuid}.kepub.epub");
        if (!File.Exists(kepubPath))
            await kepubifyConverter.Convert(sourcePath, kepubPath);
        book.KepubFilePath = kepubPath;
        book.TotalFileSize = fileRepository.GetFileSize(kepubPath);
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
