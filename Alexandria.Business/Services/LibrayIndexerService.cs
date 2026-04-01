using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Services;

public class LibraryIndexerService(AlexandriaSettings settings, IIndexFileService indexFileService, IEpubMetadataExtractor epubMetadataExtractor, IKepubifyConverter kepubifyConverter, ILogger<LibraryIndexerService> logger) : ILibraryIndexerService
{
    public async Task Index()
    {
        logger.LogInformation("Library indexing started. Books folder: {BooksFolder}", settings.BooksFolder);

        await indexFileService.LoadAsync();

        if (!Directory.Exists(settings.BooksFolder))
        {
            logger.LogWarning("Books folder does not exist: {BooksFolder}", settings.BooksFolder);
            return;
        }

        var epubFiles = Directory.GetFiles(settings.BooksFolder, "*.epub");
        logger.LogInformation("Found {Count} epub files", epubFiles.Length);

        // Remove stale entries whose source epub no longer exists on disk.
        var currentPaths = new HashSet<string>(epubFiles);
        var staleBooks = indexFileService.Books.Where(b => !currentPaths.Contains(b.SourcePath)).ToList();
        foreach (var stale in staleBooks)
        {
            logger.LogInformation("Removing stale entry: {Title} ({SourcePath})", stale.Title, stale.SourcePath);
            if (stale.CoverFilePath is not null && File.Exists(stale.CoverFilePath))
                File.Delete(stale.CoverFilePath);
            if (stale.KepubFilePath is not null && File.Exists(stale.KepubFilePath))
                File.Delete(stale.KepubFilePath);
            indexFileService.Books.Remove(stale);
        }

        foreach (var epubPath in epubFiles)
        {
            var fileHash = await indexFileService.ComputeFileHashAsync(epubPath);

            // Fast path: hash unchanged, nothing to do.
            if (indexFileService.FindByHash(fileHash) is not null)
                continue;

            // Hash is new — extract metadata to get UUID.
            var metadata = epubMetadataExtractor.ExtractMetadata(epubPath);
            var existingBook = indexFileService.FindByUuid(metadata.Uuid);

            if (existingBook is not null)
            {
                // UUID exists but hash changed — update existing entry.
                logger.LogInformation("Updating changed book: {Title}", metadata.Title);

                if (existingBook.CoverFilePath is not null && File.Exists(existingBook.CoverFilePath))
                    File.Delete(existingBook.CoverFilePath);
                if (existingBook.KepubFilePath is not null && File.Exists(existingBook.KepubFilePath))
                    File.Delete(existingBook.KepubFilePath);

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
                indexFileService.Books.Add(book);

                logger.LogInformation("Indexed: {Title} by {Authors}", book.Title, string.Join(", ", book.Authors));
            }
        }

        await indexFileService.SaveAsync();
        logger.LogInformation("Library indexing complete. Total books: {Count}", indexFileService.Books.Count);
    }
}
