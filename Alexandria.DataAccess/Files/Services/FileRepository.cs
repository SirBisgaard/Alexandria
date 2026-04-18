using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Services;

public class FileRepository(AlexandriaSettings settings, ICoverThumbnailService coverThumbnailService) : IFileRepository
{
    public async Task<byte[]?> ReadKepub(Book book)
    {
        if (book.File.KepubFilePath is null || !File.Exists(book.File.KepubFilePath))
        {
            return null;
        }
        return await File.ReadAllBytesAsync(book.File.KepubFilePath);
    }

    public async Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null)
    {
        if (width.HasValue && height.HasValue)
        {
            var thumbnail = await coverThumbnailService.GetOrCreateThumbnail(book.File.CoverFilePath, book.BookUuid, width.Value, height.Value, quality ?? 80);
            if (thumbnail is not null)
            {
                return thumbnail;
            }
        }

        if (book.File.CoverFilePath is null || !File.Exists(book.File.CoverFilePath))
        {
            return null;
        }
        return await File.ReadAllBytesAsync(book.File.CoverFilePath);
    }

    public void DeleteCover(Book book)
    {
        coverThumbnailService.DeleteThumbnails(book.BookUuid);
        if (book.File.CoverFilePath is not null && File.Exists(book.File.CoverFilePath))
        {
            File.Delete(book.File.CoverFilePath);
        }
    }

    public void DeleteKepub(Book book)
    {
        if (book.File.KepubFilePath is not null && File.Exists(book.File.KepubFilePath))
        {
            File.Delete(book.File.KepubFilePath);
        }
    }

    public string[] GetEpubFiles()
    {
        if (!Directory.Exists(settings.EbookFolder))
        {
            return [];
        }
        return Directory.GetFiles(settings.EbookFolder, "*.epub")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public long GetFileSize(string path)
    {
        return new FileInfo(path).Length;
    }
}
