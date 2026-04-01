using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Services;

public class LibraryIndexerService(AlexandriaSettings settings, IBookRepository bookRepository, IFileRepository fileRepository, IEpubMetadataExtractor epubMetadataExtractor, IKepubifyConverter kepubifyConverter, ILogger<LibraryIndexerService> logger) : ILibraryIndexerService
{
    public async Task Index()
    {
        logger.LogInformation("Library indexing started. Books folder: {BooksFolder}", settings.BooksFolder);

        var books = await bookRepository.GetAll();
        var epubFiles = fileRepository.GetEpubFiles();
        logger.LogInformation("Found {Count} epub files", epubFiles.Length);

        // Remove stale entries whose source epub no longer exists on disk.
        var currentPaths = new HashSet<string>(epubFiles);
        var staleBooks = books.Where(b => !currentPaths.Contains(b.SourcePath)).ToList();
        foreach (var stale in staleBooks)
        {
            logger.LogInformation("Removing stale entry: {Title} ({SourcePath})", stale.Title, stale.SourcePath);
            fileRepository.DeleteCover(stale);
            fileRepository.DeleteKepub(stale);
            await bookRepository.Remove(stale.Uuid);
        }

        foreach (var epubPath in epubFiles)
        {
            var fileHash = await fileRepository.ComputeFileHash(epubPath);

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

                fileRepository.DeleteCover(existingBook);
                fileRepository.DeleteKepub(existingBook);

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
                existingBook.KepubFileSize = fileRepository.GetKepubFileSize(kepubPath);
                existingBook.LastModified = DateTimeOffset.UtcNow;

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

                var now = DateTimeOffset.UtcNow;
                var book = Book.FromMetadata(metadata, fileHash, epubPath, now, now);
                book.KepubFilePath = kepubPath;
                book.KepubFileSize = fileRepository.GetKepubFileSize(kepubPath);
                await bookRepository.Add(book);

                logger.LogInformation("Indexed: {Title} by {Authors}", book.Title, string.Join(", ", book.Authors));
            }
        }

        var totalBooks = (await bookRepository.GetAll()).Count;
        logger.LogInformation("Library indexing complete. Total books: {Count}", totalBooks);
    }
}
