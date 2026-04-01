using System.Security.Cryptography;
using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Services;

public class LibraryIndexerService(AlexandriaSettings settings, IBookRepository bookRepository, IEpubMetadataExtractor epubMetadataExtractor, IKepubifyConverter kepubifyConverter, ILogger<LibraryIndexerService> logger) : ILibraryIndexerService
{
    public async Task Index()
    {
        logger.LogInformation("Library indexing started. Books folder: {BooksFolder}", settings.BooksFolder);

        var books = await bookRepository.GetAll();

        if (!Directory.Exists(settings.BooksFolder))
        {
            logger.LogWarning("Books folder does not exist: {BooksFolder}", settings.BooksFolder);
            return;
        }

        var epubFiles = Directory.GetFiles(settings.BooksFolder, "*.epub");
        logger.LogInformation("Found {Count} epub files", epubFiles.Length);

        // Remove stale entries whose source epub no longer exists on disk.
        var currentPaths = new HashSet<string>(epubFiles);
        var staleBooks = books.Where(b => !currentPaths.Contains(b.SourcePath)).ToList();
        foreach (var stale in staleBooks)
        {
            logger.LogInformation("Removing stale entry: {Title} ({SourcePath})", stale.Title, stale.SourcePath);
            if (stale.CoverFilePath is not null && File.Exists(stale.CoverFilePath))
            {
                File.Delete(stale.CoverFilePath);
            }
            if (stale.KepubFilePath is not null && File.Exists(stale.KepubFilePath))
            {
                File.Delete(stale.KepubFilePath);
            }
            await bookRepository.Remove(stale.Uuid);
        }

        foreach (var epubPath in epubFiles)
        {
            var fileHash = await ComputeFileHash(epubPath);

            // Fast path: hash unchanged, nothing to do.
            if (await bookRepository.FindByHash(fileHash) is not null)
            {
                continue;
            }

            // Hash is new — extract metadata to get UUID.
            var metadata = epubMetadataExtractor.ExtractMetadata(epubPath);
            var existingBook = await bookRepository.FindByUuid(metadata.Uuid);

            if (existingBook is not null)
            {
                // UUID exists but hash changed — update existing entry.
                logger.LogInformation("Updating changed book: {Title}", metadata.Title);

                if (existingBook.CoverFilePath is not null && File.Exists(existingBook.CoverFilePath))
                {
                    File.Delete(existingBook.CoverFilePath);
                }
                if (existingBook.KepubFilePath is not null && File.Exists(existingBook.KepubFilePath))
                {
                    File.Delete(existingBook.KepubFilePath);
                }

                var kepubPath = Path.Combine(settings.KepubsFolder, $"{metadata.Uuid}.kepub.epub");
                await kepubifyConverter.ConvertAsync(epubPath, kepubPath);

                metadata.CoverFilePath = epubMetadataExtractor.ExtractCover(epubPath, settings.CoversFolder);

                existingBook.FileHash = fileHash;
                existingBook.SourcePath = epubPath;
                existingBook.Title = metadata.Title;
                existingBook.Authors = metadata.Authors;
                existingBook.Language = metadata.Language;
                existingBook.PublicationDate = metadata.PublicationDate;
                existingBook.Description = metadata.Description;
                existingBook.Publisher = metadata.Publisher;
                existingBook.SeriesName = metadata.SeriesName;
                existingBook.SeriesIndex = metadata.SeriesIndex;
                existingBook.CoverFilePath = metadata.CoverFilePath;
                existingBook.KepubFilePath = kepubPath;
                existingBook.KepubFileSize = new FileInfo(kepubPath).Length;
                existingBook.LastModified = DateTime.UtcNow;

                await bookRepository.Update(existingBook);

                logger.LogInformation("Updated: {Title} by {Authors}", existingBook.Title, string.Join(", ", existingBook.Authors));
            }
            else
            {
                // Brand new book.
                logger.LogInformation("Indexing new book: {FileName}", Path.GetFileName(epubPath));

                var kepubPath = Path.Combine(settings.KepubsFolder, $"{metadata.Uuid}.kepub.epub");
                await kepubifyConverter.ConvertAsync(epubPath, kepubPath);
                metadata.CoverFilePath = epubMetadataExtractor.ExtractCover(epubPath, settings.CoversFolder);

                var now = DateTime.UtcNow;
                var book = Book.FromMetadata(metadata, fileHash, epubPath, now, now);
                book.KepubFilePath = kepubPath;
                book.KepubFileSize = new FileInfo(kepubPath).Length;
                await bookRepository.Add(book);

                logger.LogInformation("Indexed: {Title} by {Authors}", book.Title, string.Join(", ", book.Authors));
            }
        }

        var totalBooks = (await bookRepository.GetAll()).Count;
        logger.LogInformation("Library indexing complete. Total books: {Count}", totalBooks);
    }

    private static async Task<string> ComputeFileHash(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }
}
