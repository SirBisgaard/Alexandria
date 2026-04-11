using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Services;

public class FileRepository(AlexandriaSettings settings, ICoverThumbnailService coverThumbnailService) : IFileRepository
{
    public async Task<byte[]?> ReadKepub(Book book)
    {
        if (book.KepubFilePath is null || !File.Exists(book.KepubFilePath))
        {
            return null;
        }
        return await File.ReadAllBytesAsync(book.KepubFilePath);
    }

    public async Task<byte[]?> ReadCover(Book book, int? width = null, int? height = null, int? quality = null)
    {
        if (width.HasValue && height.HasValue)
        {
            var thumbnail = await coverThumbnailService.GetOrCreateThumbnail(book.CoverFilePath, book.BookUuid, width.Value, height.Value, quality ?? 80);
            if (thumbnail is not null)
            {
                return thumbnail;
            }
        }

        if (book.CoverFilePath is null || !File.Exists(book.CoverFilePath))
        {
            return null;
        }
        return await File.ReadAllBytesAsync(book.CoverFilePath);
    }

    public void DeleteCover(Book book)
    {
        coverThumbnailService.DeleteThumbnails(book.BookUuid);
        if (book.CoverFilePath is not null && File.Exists(book.CoverFilePath))
        {
            File.Delete(book.CoverFilePath);
        }
    }

    public void DeleteKepub(Book book)
    {
        if (book.KepubFilePath is not null && File.Exists(book.KepubFilePath))
        {
            File.Delete(book.KepubFilePath);
        }
    }

    public string[] GetEpubFiles()
    {
        if (!Directory.Exists(settings.EbookFolder))
        {
            return [];
        }
        return Directory.GetFiles(settings.EbookFolder, "*.epub");
    }

    public long GetFileSize(string path)
    {
        return new FileInfo(path).Length;
    }
}
