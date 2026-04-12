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

    /// <summary>
    /// Full library scan: discovers all book files, extracts metadata, assigns UUIDs, creates
    /// derivatives (kepub, thumbnails), and cleans up stale entries. UUID resolution priority:
    /// path match (same location) → metadata match at different path (moved file) → new UUID.
    /// This preserves BookUuid across file moves, keeping reading state and device sync intact.
    /// Unchanged books (metadata + cover identical) take a fast path that skips reprocessing.
    /// </summary>
    public async Task<int> Index(IProgress<(int current, int total)>? progress = null)
    {
        await IndexLock.WaitAsync();
        try
        {
            logger.LogInformation("Indexing library");

            var existingBooks = await bookRepository.GetAll();
            var existingByPath = new Dictionary<string, Book>(StringComparer.OrdinalIgnoreCase);
            foreach (var book in existingBooks)
                existingByPath[book.SourcePath] = book;

            var totalFiles = contentManager.GetBookFilesCount();
            var discoveredBooks = new List<Book>();
            var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var claimedUuids = new HashSet<Guid>();
            var wordsPerPage = (await settingsService.Get()).WordsPerPage;
            var fileCount = 0;

            await foreach (var (metadata, coverBytes) in contentManager.GetAllBooks())
            {
                progress?.Report((++fileCount, totalFiles));
                discoveredPaths.Add(metadata.SourcePath);

                var existingAtPath = existingByPath.GetValueOrDefault(metadata.SourcePath);

                // Unchanged — metadata + cover both match existing.
                if (existingAtPath is not null && existingAtPath.MetadataEquals(metadata) && CoverUnchanged(existingAtPath, coverBytes))
                {
                    discoveredBooks.Add(existingAtPath);
                    await contentManager.CreateDerivativeFiles(existingAtPath);
                    claimedUuids.Add(existingAtPath.BookUuid);
                    continue;
                }

                // Resolve UUID: path match → moved book match → new.
                var bookUuid =
                    existingAtPath?.BookUuid ??
                    FindMovedBookUuid(metadata, existingBooks, discoveredPaths) ??
                    metadata.GenerateBookUuid();

                metadata.BookUuid = bookUuid;
                metadata.CoverFilePath = contentManager.SaveCover(metadata.SourcePath, coverBytes, bookUuid);
                var book = metadata;
                discoveredBooks.Add(book);
                await contentManager.CreateDerivativeFiles(book);

                // Classify and persist.
                var existingMatch = existingAtPath ?? existingBooks.FirstOrDefault(b => b.BookUuid == bookUuid);
                if (existingMatch is not null && existingAtPath is not null)
                {
                    logger.LogInformation("Reindexing externally modified file: {Title} ({SourcePath})", existingMatch.Title, metadata.SourcePath);
                    await HandleExistingBook(book, existingMatch, wordsPerPage);
                }
                else if (existingMatch is not null)
                {
                    logger.LogInformation("Reindexing moved file: {Title} ({OldPath} -> {NewPath})", existingMatch.Title, existingMatch.SourcePath, metadata.SourcePath);
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
            logger.LogInformation("Library indexing complete, {Count} files total", discoveredBooks.Count);
            return discoveredBooks.Count;
        }
        finally
        {
            IndexLock.Release();
        }
    }

    /// <summary>
    /// Re-extracts metadata from a single book's source file after an edit. If desiredMetadata or
    /// coverUpdate is provided, embeds those changes into the source file first. Preserves the
    /// original EntitlementUuid and CreatedAt so existing device syncs and reading state remain valid.
    /// </summary>
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
            PreserveUserEdits(refreshed, desiredMetadata ?? existing);
            var wordsPerPage = (await settingsService.Get()).WordsPerPage;
            ComputePageCount(refreshed, wordsPerPage);

            await PersistBook(refreshed, false);
            await databasePersistence.Flush();

            logger.LogInformation("Reindexing book: {mediaType}:{Title} from {filePath}", refreshed.MediaType, refreshed.Title, refreshed.SourcePath);
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

        logger.LogInformation("Indexing new book: {mediaType}:{Title} from {filePath}", discovered.MediaType, discovered.Title, discovered.SourcePath);
    }

    private async Task HandleExistingBook(Book discovered, Book existing, int wordsPerPage)
    {
        logger.LogInformation("Updating changed book: {Title} ({SourcePath})", discovered.Title, discovered.SourcePath);

        discovered.EntitlementUuid = existing.EntitlementUuid;
        discovered.CreatedAt = existing.CreatedAt;
        discovered.ModifiedAt = DateTimeOffset.UtcNow;
        PreserveUserEdits(discovered, existing);
        ComputePageCount(discovered, wordsPerPage);

        await PersistBook(discovered, false);
    }

    private async Task HandleMovedBook(Book discovered, Book existing, int wordsPerPage)
    {
        logger.LogInformation("Updating moved book: {Title} ({SourcePath})", discovered.Title, discovered.SourcePath);

        discovered.EntitlementUuid = existing.EntitlementUuid;
        discovered.CreatedAt = existing.CreatedAt;
        discovered.ModifiedAt = DateTimeOffset.UtcNow;
        PreserveUserEdits(discovered, existing);
        ComputePageCount(discovered, wordsPerPage);

        await PersistBook(discovered, false);
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

    /// <summary>
    /// Detects moved files by matching metadata (title, authors, etc.) against existing books whose
    /// source paths no longer exist on disk. Prevents duplicate entries when files are reorganized.
    /// </summary>
    private static Guid? FindMovedBookUuid(Book freshMetadata, List<Book> existingBooks, HashSet<string> discoveredPaths)
    {
        return existingBooks.FirstOrDefault(b => !discoveredPaths.Contains(b.SourcePath) && b.MetadataEquals(freshMetadata))?.BookUuid;
    }

    /// <summary>
    /// Restores user-edited fields that can't round-trip through the source file format. EPUB supports
    /// all Dublin Core fields so nothing extra is needed. M4B only supports standard ID3/MP4 tags, so
    /// Language, Identifiers, Source, Type, Relation, Coverage, and Format are preserved from the database.
    /// </summary>
    private static void PreserveUserEdits(Book discovered, Book existing)
    {
        discovered.Subtitle = existing.Subtitle;
        discovered.IsAbridged = existing.IsAbridged;

        if (discovered.MediaType == MediaType.Ebook)
        {
            // EPUB provides all Dublin Core fields — nothing extra to preserve
        }
        else if (discovered.MediaType == MediaType.Audiobook)
        {
            // M4B only supports standard ID3/MP4 tags — fields not embeddable are preserved from DB
            if (!string.IsNullOrEmpty(existing.Language))
                discovered.Language = existing.Language;
            if (discovered.Identifiers.Count == 0 && existing.Identifiers.Count > 0)
                discovered.Identifiers = existing.Identifiers;
            if (string.IsNullOrEmpty(discovered.Source))
                discovered.Source = existing.Source;
            if (string.IsNullOrEmpty(discovered.Type))
                discovered.Type = existing.Type;
            if (string.IsNullOrEmpty(discovered.Relation))
                discovered.Relation = existing.Relation;
            if (string.IsNullOrEmpty(discovered.Coverage))
                discovered.Coverage = existing.Coverage;
            if (string.IsNullOrEmpty(discovered.Format))
                discovered.Format = existing.Format;
        }
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
