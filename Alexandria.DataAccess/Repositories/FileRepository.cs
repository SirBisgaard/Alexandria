using System.Security.Cryptography;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;

namespace Alexandria.DataAccess.Repositories;

public class FileRepository(AlexandriaSettings settings) : IFileRepository
{
    public async Task<byte[]?> ReadKepub(Book book)
    {
        if (book.KepubFilePath is null || !File.Exists(book.KepubFilePath))
        {
            return null;
        }
        return await File.ReadAllBytesAsync(book.KepubFilePath);
    }

    public async Task<byte[]?> ReadCover(Book book)
    {
        if (book.CoverFilePath is null || !File.Exists(book.CoverFilePath))
        {
            return null;
        }
        return await File.ReadAllBytesAsync(book.CoverFilePath);
    }

    public void DeleteCover(Book book)
    {
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
        if (!Directory.Exists(settings.BooksFolder))
        {
            return [];
        }
        return Directory.GetFiles(settings.BooksFolder, "*.epub");
    }

    public async Task<string> ComputeFileHash(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }

    public long GetKepubFileSize(string kepubPath)
    {
        return new FileInfo(kepubPath).Length;
    }
}
