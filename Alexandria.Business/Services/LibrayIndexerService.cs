using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Files;
using Alexandria.Business.Models.Settings;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Services;

public class LibraryIndexerService(AlexandriaSettings settings, IBookRepository bookRepository, IFileRepository fileRepository, IEpubMetadataHandler epubMetadataHandler, IKepubifyConverter kepubifyConverter, ILogger<LibraryIndexerService> logger) : ILibraryIndexerService
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
            var metadata = epubMetadataHandler.ExtractMetadata(epubPath);
            var existingBook = await bookRepository.FindByUuid(metadata.Uuid);

            if (existingBook is not null)
            {
                logger.LogInformation("Updating changed book: {Title}", metadata.Title);
                await RebuildBook(existingBook, epubPath, fileHash, metadata);
                existingBook.LastModified = DateTimeOffset.UtcNow;
                await bookRepository.Update(existingBook);
                logger.LogInformation("Updated: {Title} by {Authors}", existingBook.Title, string.Join(", ", existingBook.Authors));
            }
            else
            {
                logger.LogInformation("Indexing new book: {FileName}", Path.GetFileName(epubPath));
                var now = DateTimeOffset.UtcNow;
                var book = Book.FromMetadata(metadata, fileHash, epubPath, now, now);
                await RebuildBook(book, epubPath, fileHash, metadata);
                book.CreatedAt = now;
                book.LastModified = now;
                await bookRepository.Add(book);
                logger.LogInformation("Indexed: {Title} by {Authors}", book.Title, string.Join(", ", book.Authors));
            }
        }

        var totalBooks = (await bookRepository.GetAll()).Count;
        logger.LogInformation("Library indexing complete. Total books: {Count}", totalBooks);
    }

    public async Task ReindexBook(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid)
            ?? throw new InvalidOperationException($"Book with UUID '{uuid}' not found.");

        logger.LogInformation("Reindexing book: {Title}", book.Title);

        var fileHash = await fileRepository.ComputeFileHash(book.SourcePath);
        var metadata = epubMetadataHandler.ExtractMetadata(book.SourcePath);

        await RebuildBook(book, book.SourcePath, fileHash, metadata);
        book.LastModified = DateTimeOffset.UtcNow;
        await bookRepository.Update(book);

        logger.LogInformation("Reindexed: {Title} by {Authors}", book.Title, string.Join(", ", book.Authors));
    }

    private async Task RebuildBook(Book book, string epubPath, string fileHash, EpubMetadata metadata)
    {
        fileRepository.DeleteCover(book);
        fileRepository.DeleteKepub(book);

        var kepubPath = Path.Combine(settings.KepubsFolder, $"{book.Uuid}.kepub.epub");
        await kepubifyConverter.ConvertAsync(epubPath, kepubPath);

        metadata.CoverFilePath = epubMetadataHandler.ExtractCover(epubPath, settings.CoversFolder);

        book.FileHash = fileHash;
        book.SourcePath = epubPath;
        book.Title = metadata.Title;
        book.Authors = metadata.Authors;
        book.Language = metadata.Language;
        book.PublicationDate = metadata.PublicationDate;
        book.Description = metadata.Description;
        book.Publisher = metadata.Publisher;
        book.SeriesName = metadata.SeriesName;
        book.SeriesIndex = metadata.SeriesIndex;
        book.CoverFilePath = metadata.CoverFilePath;
        book.KepubFilePath = kepubPath;
        book.KepubFileSize = fileRepository.GetKepubFileSize(kepubPath);
    }
}
