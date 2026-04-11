using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Library.Services;

public class AlexandriaIndexerService(IFileContentManager contentManager, IBookRepository bookRepository, IAlexandriaSettingsService settingsService, IAuthorRepository authorRepository, ISeriesRepository seriesRepository, IDatabasePersistence databasePersistence, ILogger<AlexandriaIndexerService> logger) : ILibraryIndexerService
{
    private static readonly SemaphoreSlim IndexLock = new(1, 1);

    public async Task<int> Index(IProgress<(int current, int total)>? progress = null)
    {
        await IndexLock.WaitAsync();
        try
        {
            logger.LogInformation("Library indexing started.");

            var existingBooks = await bookRepository.GetAll();
            var existingByPath = new Dictionary<string, Book>(StringComparer.OrdinalIgnoreCase);
            foreach (var book in existingBooks)
                existingByPath[book.SourcePath] = book;

            var files = contentManager.GetContentFiles();
            var discoveredBooks = new List<Book>();
            var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var claimedUuids = new HashSet<Guid>();
            var wordsPerPage = (await settingsService.Get()).WordsPerPage;

            for (var i = 0; i < files.Length; i++)
            {
                var filePath = files[i];
                progress?.Report((i + 1, files.Length * 2));
                discoveredPaths.Add(filePath);

                var existingAtPath = existingByPath.GetValueOrDefault(filePath);
                var metadata = contentManager.ExtractMetadata(filePath);

                // Unchanged — metadata + cover both match existing.
                if (existingAtPath is not null && existingAtPath.MetadataEquals(metadata) && CoverUnchanged(existingAtPath, contentManager.ExtractCoverBytes(filePath)))
                {
                    discoveredBooks.Add(existingAtPath);
                    await contentManager.CreateDerivativeFiles(existingAtPath);
                    claimedUuids.Add(existingAtPath.BookUuid);
                    continue;
                }

                // Resolve UUID: path match → moved book match → new.
                var bookUuid = existingAtPath?.BookUuid ?? FindMovedBookUuid(metadata, existingBooks, discoveredPaths) ?? Guid.NewGuid();

                var (book, _) = contentManager.ExtractBook(filePath, bookUuid);
                discoveredBooks.Add(book);
                await contentManager.CreateDerivativeFiles(book);

                // Classify and persist.
                var existingMatch = existingAtPath ?? existingBooks.FirstOrDefault(b => b.BookUuid == bookUuid);
                if (existingMatch is not null && existingAtPath is not null)
                {
                    logger.LogInformation("File modified externally: {Title} ({SourcePath})", existingMatch.Title, filePath);
                    await HandleExistingBook(book, existingMatch, wordsPerPage);
                }
                else if (existingMatch is not null)
                {
                    logger.LogInformation("File moved: {Title} ({OldPath} -> {NewPath})", existingMatch.Title, existingMatch.SourcePath, filePath);
                    await HandleMovedBook(book, existingMatch, wordsPerPage);
                }
                else
                {
                    await HandleNewBook(book, wordsPerPage);
                }

                claimedUuids.Add(bookUuid);
            }

            contentManager.CleanupStaleDerivatives(discoveredBooks, existingBooks);
            await HandleStaleBooks(existingBooks, claimedUuids);
            await settingsService.UpdateLastIndexedTimestamp(DateTimeOffset.UtcNow);
            await databasePersistence.Flush();
            logger.LogInformation("Library indexing complete. Total files: {Count}", discoveredBooks.Count);
            return discoveredBooks.Count;
        }
        finally
        {
            IndexLock.Release();
        }
    }

    public async Task ReindexBook(Guid uuid, Book? desiredMetadata = null, CoverUpdate? coverUpdate = null)
    {
        await IndexLock.WaitAsync();
        try
        {
            var existing = await bookRepository.FindByUuid(uuid)
                ?? throw new InvalidOperationException($"Book with UUID '{uuid}' not found.");

            if (desiredMetadata is not null || coverUpdate is not null)
            {
                var bookToUpdate = desiredMetadata ?? existing;
                bookToUpdate.SourcePath = existing.SourcePath;
                await contentManager.UpdateBook(bookToUpdate, coverUpdate);
            }

            var refreshed = await contentManager.GetBook(existing);
            refreshed.EntitlementUuid = existing.EntitlementUuid;
            refreshed.CreatedAt = existing.CreatedAt;
            refreshed.ModifiedAt = DateTimeOffset.UtcNow;
            MergeDbOnlyFields(refreshed, desiredMetadata ?? existing);
            var wordsPerPage = (await settingsService.Get()).WordsPerPage;
            ComputePageCount(refreshed, wordsPerPage);

            await PersistBook(refreshed, false);
            await databasePersistence.Flush();

            logger.LogInformation("Reindexed: {mediaType}:{Title} from {filePath}", refreshed.MediaType, refreshed.Title, refreshed.SourcePath);
        }
        finally
        {
            IndexLock.Release();
        }
    }

    private async Task HandleNewBook(Book discovered, int wordsPerPage)
    {
        var now = DateTimeOffset.UtcNow;

        discovered.EntitlementUuid = Guid.NewGuid();
        discovered.CreatedAt = now;
        discovered.ModifiedAt = now;
        ComputePageCount(discovered, wordsPerPage);

        await PersistBook(discovered, true);

        logger.LogInformation("Indexed: {mediaType}:{Title} from {filePath}", discovered.MediaType, discovered.Title, discovered.SourcePath);
    }

    private async Task HandleExistingBook(Book discovered, Book existing, int wordsPerPage)
    {
        logger.LogInformation("Updating changed book: {Title}", discovered.Title);

        discovered.EntitlementUuid = existing.EntitlementUuid;
        discovered.CreatedAt = existing.CreatedAt;
        discovered.ModifiedAt = DateTimeOffset.UtcNow;
        MergeDbOnlyFields(discovered, existing);
        ComputePageCount(discovered, wordsPerPage);

        await PersistBook(discovered, false);

        logger.LogInformation("Updated: {mediaType}:{Title} from {filePath}", discovered.MediaType, discovered.Title, discovered.SourcePath);
    }

    private async Task HandleMovedBook(Book discovered, Book existing, int wordsPerPage)
    {
        logger.LogInformation("Updating moved book: {Title}", discovered.Title);

        discovered.EntitlementUuid = existing.EntitlementUuid;
        discovered.CreatedAt = existing.CreatedAt;
        discovered.ModifiedAt = DateTimeOffset.UtcNow;
        MergeDbOnlyFields(discovered, existing);
        ComputePageCount(discovered, wordsPerPage);

        await PersistBook(discovered, false);

        logger.LogInformation("Moved: {mediaType}:{Title} from {filePath}", discovered.MediaType, discovered.Title, discovered.SourcePath);
    }

    private async Task PersistBook(Book book, bool isNewBook)
    {
        foreach (var author in book.Authors)
            await authorRepository.GetOrCreate(author.Name);
        if (book.Series is not null)
            await seriesRepository.GetOrCreate(book.Series.Name);

        if (isNewBook)
            await bookRepository.Add(book);
        else
            await bookRepository.Update(book);

        await authorRepository.SetBookAuthors(book.BookUuid, book.Authors.Select(a => a.Name).ToList());
        book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);

        if (book.Series is not null)
        {
            var seriesUuid = await seriesRepository.GetOrCreate(book.Series.Name);
            await seriesRepository.SetBookSeries(book.BookUuid, seriesUuid, book.Series.Index);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
        }
        else
        {
            await seriesRepository.RemoveBookSeries(book.BookUuid);
            book.Series = null;
        }
    }

    private static bool CoverUnchanged(Book existing, byte[]? freshCoverBytes)
    {
        if (existing.CoverFilePath is null && freshCoverBytes is null)
            return true;
        if (existing.CoverFilePath is null || freshCoverBytes is null)
            return false;
        if (!File.Exists(existing.CoverFilePath))
            return false;
        var existingBytes = File.ReadAllBytes(existing.CoverFilePath);
        return existingBytes.AsSpan().SequenceEqual(freshCoverBytes);
    }

    private static Guid? FindMovedBookUuid(Book freshMetadata, List<Book> existingBooks, HashSet<string> discoveredPaths)
    {
        return existingBooks.FirstOrDefault(b => !discoveredPaths.Contains(b.SourcePath) && b.MetadataEquals(freshMetadata))?.BookUuid;
    }

    private static void MergeDbOnlyFields(Book target, Book source)
    {
        if (string.IsNullOrEmpty(target.Subtitle))
            target.Subtitle = source.Subtitle;
        if (string.IsNullOrEmpty(target.Language))
            target.Language = source.Language;
        if (string.IsNullOrEmpty(target.Publisher))
            target.Publisher = source.Publisher;
        if (target.IsAbridged is null)
            target.IsAbridged = source.IsAbridged;
        if (target.Series is null && source.Series is not null)
            target.Series = source.Series;
        if (target.Identifiers.Count == 0 && source.Identifiers.Count > 0)
            target.Identifiers = source.Identifiers;
    }

    private static void ComputePageCount(Book book, int wordsPerPage)
    {
        if (book.WordCount is not null)
            book.PageCount = (int)Math.Ceiling((double)book.WordCount.Value / wordsPerPage);
    }

    private async Task HandleStaleBooks(List<Book> existingBooks, HashSet<Guid> claimedUuids)
    {
        var staleBooks = existingBooks.Where(b => !claimedUuids.Contains(b.BookUuid)).ToList();
        foreach (var stale in staleBooks)
        {
            logger.LogInformation("Removing stale entry: {Title} ({SourcePath})", stale.Title, stale.SourcePath);
            await bookRepository.Remove(stale.BookUuid);
        }

        if (staleBooks.Count > 0)
        {
            await authorRepository.RemoveOrphanedAuthors();
            await seriesRepository.RemoveOrphanedSeries();
        }
    }
}
