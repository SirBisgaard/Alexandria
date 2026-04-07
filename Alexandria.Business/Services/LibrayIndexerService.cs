using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Files;
using Alexandria.Business.Models.Settings;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Services;

public class LibraryIndexerService(AlexandriaSettings settings, IBookRepository bookRepository, IFileRepository fileRepository, IEpubMetadataHandler epubMetadataHandler, IKepubifyConverter kepubifyConverter, IAlexandriaSettingsService settingsService, IAuthorRepository authorRepository, ISeriesRepository seriesRepository, ICoverThumbnailService coverThumbnailService, ILogger<LibraryIndexerService> logger) : ILibraryIndexerService
{
    private static readonly SemaphoreSlim IndexLock = new(1, 1);

    public async Task<int> Index(IProgress<(int current, int total)>? progress = null)
    {
        await IndexLock.WaitAsync();
        try
        {
            logger.LogInformation("Library indexing started. Books folder: {BooksFolder}", settings.BooksFolder);

            var books = await bookRepository.GetAll();
            var epubFiles = fileRepository.GetEpubFiles();
            logger.LogInformation("Found {Count} epub files", epubFiles.Length);

            await HandleStaleBooks(epubFiles, books);

            for (var i = 0; i < epubFiles.Length; i++)
            {
                progress?.Report((i + 1, epubFiles.Length));
                var epubPath = epubFiles[i];
                var fileHash = await fileRepository.ComputeFileHash(epubPath);

                // Fast path: hash unchanged, nothing to do.
                if (await bookRepository.FindByHash(fileHash) is not null)
                {
                    continue;
                }

                // Slow path: extract metadata + cover in a single ZIP read.
                var metadata = epubMetadataHandler.ExtractAll(epubPath, settings.CoversFolder);
                metadata.CoverFilePath = ConvertCoverToJpeg(metadata.CoverFilePath);

                // Stamp with alexandria:uuid if not already present.
                if (metadata.AlexandriaUuid is null)
                {
                    metadata.AlexandriaUuid = metadata.DeterministicUuid;
                    logger.LogInformation("Stamping EPUB with alexandria:uuid: {Uuid}", metadata.AlexandriaUuid);
                    epubMetadataHandler.EmbedMetadata(epubPath, metadata);
                    fileHash = await fileRepository.ComputeFileHash(epubPath);
                }

                var bookUuid = metadata.AlexandriaUuid ?? metadata.DeterministicUuid;
                var existingBook = await bookRepository.FindByUuid(bookUuid);

                if (existingBook is not null)
                {
                    await HandleExistingEpub(metadata, epubPath, fileHash, existingBook);
                }
                else
                {
                    await HandleNewEpub(epubPath, metadata, fileHash);
                }
            }

            await settingsService.UpdateLastIndexedTimestamp(DateTimeOffset.UtcNow);
            logger.LogInformation("Library indexing complete. Total books: {Count}", epubFiles.Length);
            return epubFiles.Length;
        }
        finally
        {
            IndexLock.Release();
        }
    }

    public async Task ReindexBook(Guid uuid)
    {
        await IndexLock.WaitAsync();
        try
        {
            var book = await bookRepository.FindByUuid(uuid)
                ?? throw new InvalidOperationException($"Book with UUID '{uuid}' not found.");

            logger.LogInformation("Reindexing book: {Title}", book.Title);

            var fileHash = await fileRepository.ComputeFileHash(book.SourcePath);

            await HandleExistingEpub(book.ToEpubMetadata(), book.SourcePath, fileHash, book);

            logger.LogInformation("Reindexed: {Title} by {Authors}", book.Title, string.Join(", ", book.Authors.Select(a => a.Name)));
        }
        finally
        {
            IndexLock.Release();
        }
    }

    private async Task HandleNewEpub(string epubPath, EpubMetadata metadata, string fileHash)
    {
        logger.LogInformation("Indexing new book: {FileName}", Path.GetFileName(epubPath));
        var now = DateTimeOffset.UtcNow;
        var book = Book.FromMetadata(metadata, fileHash, epubPath, now, now);

        var kepubPath = Path.Combine(settings.KepubsFolder, $"{book.BookUuid}.kepub.epub");
        await kepubifyConverter.Convert(epubPath, kepubPath);

        book.CoverFilePath = metadata.CoverFilePath;
        book.KepubFilePath = kepubPath;
        book.KepubFileSize = fileRepository.GetKepubFileSize(kepubPath);

        await PersistBook(metadata, book, true);

        logger.LogInformation("Indexed: {Title} by {Authors}", book.Title, string.Join(", ", metadata.Authors));
    }

    private async Task HandleExistingEpub(EpubMetadata metadata, string epubPath, string fileHash, Book existingBook)
    {
        logger.LogInformation("Updating changed book: {Title}", metadata.Title);

        // Delete old derived files BEFORE extracting new ones (cover path may be the same).
        fileRepository.DeleteCover(existingBook);
        fileRepository.DeleteKepub(existingBook);

        // Re-extract metadata + cover now that old files are gone.
        var freshMetadata = epubMetadataHandler.ExtractAll(epubPath, settings.CoversFolder);
        freshMetadata.CoverFilePath = ConvertCoverToJpeg(freshMetadata.CoverFilePath);

        var kepubPath = Path.Combine(settings.KepubsFolder, $"{existingBook.BookUuid}.kepub.epub");
        await kepubifyConverter.Convert(epubPath, kepubPath);

        existingBook.FileHash = fileHash;
        existingBook.CoverImageUuid = Guid.GenerateUuidV5("cover:" + fileHash);
        existingBook.SourcePath = epubPath;
        existingBook.Title = freshMetadata.Title;
        existingBook.Language = freshMetadata.Language;
        existingBook.PublicationDate = freshMetadata.PublicationDate;
        existingBook.Description = freshMetadata.Description;
        existingBook.Publisher = freshMetadata.Publisher;
        existingBook.CoverFilePath = freshMetadata.CoverFilePath;
        existingBook.KepubFilePath = kepubPath;
        existingBook.KepubFileSize = fileRepository.GetKepubFileSize(kepubPath);
        existingBook.ModifiedAt = DateTimeOffset.UtcNow;

        await PersistBook(freshMetadata, existingBook, false);

        logger.LogInformation("Updated: {Title} by {Authors}", existingBook.Title, string.Join(", ", existingBook.Authors.Select(a => a.Name)));
    }

    private async Task PersistBook(EpubMetadata metadata, Book book, bool isNewBook)
    {
        foreach (var authorName in metadata.Authors)
            await authorRepository.GetOrCreate(authorName);
        if (metadata.SeriesName is not null)
            await seriesRepository.GetOrCreate(metadata.SeriesName);

        if (isNewBook)
            await bookRepository.Add(book);
        else
            await bookRepository.Update(book);
        
        await authorRepository.SetBookAuthors(book.BookUuid, metadata.Authors);
        book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
        
        if (metadata.SeriesName is not null)
        {
            var seriesUuid = await seriesRepository.GetOrCreate(metadata.SeriesName);
            await seriesRepository.SetBookSeries(book.BookUuid, seriesUuid, metadata.SeriesIndex);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
        }
        else
        {
            await seriesRepository.RemoveBookSeries(book.BookUuid);
            book.Series = null;
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

    private async Task HandleStaleBooks(string[] epubFiles, List<Book> books)
    {
        var currentPaths = new HashSet<string>(epubFiles);
        var staleBooks = books.Where(b => !currentPaths.Contains(b.SourcePath)).ToList();
        foreach (var stale in staleBooks)
        {
            logger.LogInformation("Removing stale entry: {Title} ({SourcePath})", stale.Title, stale.SourcePath);
            fileRepository.DeleteCover(stale);
            fileRepository.DeleteKepub(stale);
            await bookRepository.Remove(stale.BookUuid);
        }

        if (staleBooks.Count > 0)
        {
            await authorRepository.RemoveOrphanedAuthors();
            await seriesRepository.RemoveOrphanedSeries();
        }
    }
}
