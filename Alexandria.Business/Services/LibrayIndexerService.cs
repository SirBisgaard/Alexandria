using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Services;

public class LibraryIndexerService(AlexandriaSettings settings, IIndexFileService indexFileService, IEpubMetadataExstractor epubMetadataExstractor, IKepubifyConverter kepubifyConverter, ILogger<LibraryIndexerService> logger) : ILibraryIndexerService
{
    public async Task Index()
    {
        logger.LogInformation("Library indexing started. Books folder: {BooksFolder}", settings.BooksFolder);

        // Load index file.
        await indexFileService.LoadAsync();

        // Load book files in book folder.
        if (!Directory.Exists(settings.BooksFolder))
        {
            logger.LogWarning("Books folder does not exist: {BooksFolder}", settings.BooksFolder);
            return;
        }

        var epubFiles = Directory.GetFiles(settings.BooksFolder, "*.epub");
        logger.LogInformation("Found {Count} epub files", epubFiles.Length);

        // Remove stale entries whose source epub no longer exists or whose hash changed.
        var currentHashes = new HashSet<string>();
        foreach (var epubPath in epubFiles)
            currentHashes.Add(await indexFileService.ComputeFileHashAsync(epubPath));

        var staleBooks = indexFileService.Books.Where(b => !currentHashes.Contains(b.FileHash)).ToList();
        foreach (var stale in staleBooks)
        {
            logger.LogInformation("Removing stale entry: {Title} ({Hash})", stale.Title, stale.FileHash);
            if (stale.CoverFilePath is not null && File.Exists(stale.CoverFilePath))
                File.Delete(stale.CoverFilePath);
            if (stale.KepubFilePath is not null && File.Exists(stale.KepubFilePath))
                File.Delete(stale.KepubFilePath);
            indexFileService.Books.Remove(stale);
        }

        foreach (var epubPath in epubFiles)
        {
            // Convert new or updated books. (SHA hash not matched)
            var fileHash = await indexFileService.ComputeFileHashAsync(epubPath);
            if (indexFileService.FindByHash(fileHash) is not null)
                continue;

            logger.LogInformation("Indexing new book: {FileName}", Path.GetFileName(epubPath));

            // Extract new or updated cover photos (SHA hash not matched)
            var metadata = epubMetadataExstractor.Extract(epubPath, settings.CoversFolder);

            // Convert to KEPUB.
            var kepubPath = Path.Combine(settings.KepubsFolder, $"{metadata.Uuid}.kepub.epub");
            await kepubifyConverter.ConvertAsync(epubPath, kepubPath);

            var book = Book.FromMetadata(metadata, fileHash, epubPath);
            book.KepubFilePath = kepubPath;
            indexFileService.Books.Add(book);

            logger.LogInformation("Indexed: {Title} by {Authors}", book.Title, string.Join(", ", book.Authors));
        }

        // Update index file with changes.
        await indexFileService.SaveAsync();
        logger.LogInformation("Library indexing complete. Total books: {Count}", indexFileService.Books.Count);
    }
}
