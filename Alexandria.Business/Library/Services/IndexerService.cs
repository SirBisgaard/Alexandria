using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Library.Services;

public class IndexerService(IFileContentManager contentManager, IBookRepository bookRepository, ISettingsService settingsService, IAuthorRepository authorRepository, ISeriesRepository seriesRepository, IEnumerable<IFileConverter> converters, IProgressService progressService, ILogger<IndexerService> logger) : IIndexerService
{
    private static readonly SemaphoreSlim IndexLock = new(1, 1);

    /// <summary>
    /// Full library scan: discovers all book files, extracts metadata, assigns UUIDs, creates
    /// derivatives (kepub, thumbnails), and cleans up stale entries. UUID resolution priority:
    /// path match (same location) → metadata match at different path (moved file) → new UUID.
    /// This preserves BookUuid across file moves, keeping reading state and device sync intact.
    /// Unchanged books (metadata + cover identical and file mtime not newer than the stored
    /// ModifiedAt) take a fast path that skips reprocessing.
    /// Progress is published through <see cref="IProgressService"/> — the scoped
    /// consumer must call <c>Subscribe</c> before invoking this method to receive events.
    /// </summary>
    public async Task<int> Index()
    {
        await IndexLock.WaitAsync();
        try
        {
            logger.LogInformation("Indexing library");

            var existingBooks = await bookRepository.GetAll();

            // Converters run before file discovery so any newly-converted output is picked up
            // by the regular indexers on the same run. Per-book failures are handled inside
            // the converter (e.g. quarantining the source file); they do not abort indexing.
            foreach (var converter in converters)
                await converter.Convert(existingBooks, CancellationToken.None);


            var existingByPath = new Dictionary<string, Book>(StringComparer.OrdinalIgnoreCase);
            foreach (var book in existingBooks)
                existingByPath[book.File.SourcePath] = book;

            var totalFiles = contentManager.GetBookFilesCount();
            var discoveredBooks = new List<Book>();
            var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var claimedUuids = new HashSet<Guid>();
            var wordsPerPage = (await settingsService.Get()).WordsPerPage;
            var fileCount = 0;

            await foreach (var (metadata, coverBytes) in contentManager.GetAllBooks())
            {
                progressService.Report("Indexing", ++fileCount, totalFiles);
                discoveredPaths.Add(metadata.File.SourcePath);

                var existingAtPath = existingByPath.GetValueOrDefault(metadata.File.SourcePath);

                // Unchanged — metadata + cover both match existing and file mtime hasn't advanced past ModifiedAt.
                if (existingAtPath is not null && !FileChangedSince(metadata.File.SourcePath, existingAtPath.ModifiedAt) && existingAtPath.MetadataEquals(metadata) && CoverUnchanged(existingAtPath, coverBytes))
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
                metadata.File.CoverFilePath = contentManager.SaveCover(metadata.File.SourcePath, coverBytes, bookUuid);
                var book = metadata;
                var existingMatch = existingAtPath ?? existingBooks.FirstOrDefault(b => b.BookUuid == bookUuid);
                if (existingMatch is not null)
                    PreserveUserEdits(book, existingMatch);
                discoveredBooks.Add(book);
                await contentManager.CreateDerivativeFiles(book);

                // Classify and persist.
                if (existingMatch is not null && existingAtPath is not null)
                {
                    logger.LogInformation("Reindexing externally modified file: {Title} ({SourcePath})", existingMatch.Title, metadata.File.SourcePath);
                    await HandleExistingBook(book, existingMatch, wordsPerPage);
                }
                else if (existingMatch is not null)
                {
                    logger.LogInformation("Reindexing moved file: {Title} ({OldPath} -> {NewPath})", existingMatch.Title, existingMatch.File.SourcePath, metadata.File.SourcePath);
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
                bookToUpdate.File.SourcePath = existing.File.SourcePath;
                await contentManager.UpdateBook(bookToUpdate, coverUpdate);
            }

            var refreshed = await contentManager.GetBook(desiredMetadata ?? existing);
            refreshed.EntitlementUuid = existing.EntitlementUuid;
            refreshed.CreatedAt = existing.CreatedAt;
            refreshed.ModifiedAt = DateTimeOffset.UtcNow;
            PreserveUserEdits(refreshed, desiredMetadata ?? existing);
            var wordsPerPage = (await settingsService.Get()).WordsPerPage;
            ComputePageCount(refreshed, wordsPerPage);

            await PersistBook(refreshed, false);

            logger.LogInformation("Reindexing book: {mediaType}:{Title} from {filePath}", refreshed.File.MediaType, refreshed.Title, refreshed.File.SourcePath);
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

        logger.LogInformation("Indexing new book: {mediaType}:{Title} from {filePath}", discovered.File.MediaType, discovered.Title, discovered.File.SourcePath);
    }

    private async Task HandleExistingBook(Book discovered, Book existing, int wordsPerPage)
    {
        logger.LogInformation("Updating changed book: {Title} ({SourcePath})", discovered.Title, discovered.File.SourcePath);

        discovered.EntitlementUuid = existing.EntitlementUuid;
        discovered.CreatedAt = existing.CreatedAt;
        discovered.ModifiedAt = DateTimeOffset.UtcNow;
        PreserveUserEdits(discovered, existing);
        ComputePageCount(discovered, wordsPerPage);

        await PersistBook(discovered, false);
    }

    private async Task HandleMovedBook(Book discovered, Book existing, int wordsPerPage)
    {
        logger.LogInformation("Updating moved book: {Title} ({SourcePath})", discovered.Title, discovered.File.SourcePath);

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

    private static bool FileChangedSince(string path, DateTimeOffset since)
    {
        return File.GetLastWriteTimeUtc(path) > since.UtcDateTime;
    }

    private static bool CoverUnchanged(Book existing, byte[]? freshCoverBytes)
    {
        if (existing.File.CoverFilePath is null && freshCoverBytes is null)
            return true;
        if (existing.File.CoverFilePath is null || freshCoverBytes is null)
            return false;
        if (!File.Exists(existing.File.CoverFilePath))
            return false;
        var existingBytes = File.ReadAllBytes(existing.File.CoverFilePath);
        return existingBytes.AsSpan().SequenceEqual(freshCoverBytes);
    }

    /// <summary>
    /// Detects moved files by matching metadata (title, authors, etc.) against existing books whose
    /// source paths no longer exist on disk. Prevents duplicate entries when files are reorganized.
    /// </summary>
    private static Guid? FindMovedBookUuid(Book freshMetadata, List<Book> existingBooks, HashSet<string> discoveredPaths)
    {
        return existingBooks.FirstOrDefault(b => !discoveredPaths.Contains(b.File.SourcePath) && b.MetadataEquals(freshMetadata))?.BookUuid;
    }

    /// <summary>
    /// Restores user-edited fields that can't round-trip through the source file format. EPUB supports
    /// all Dublin Core fields so nothing extra is needed. M4B only supports standard ID3/MP4 tags, so
    /// Language, Identifiers, Source, Type, Relation, Coverage, and Format are preserved from the database.
    /// ComicInfo.xml has no slot for Rights, Source, Type, Relation, Coverage, Format, Identifiers or the
    /// comic colour mode, so those always come from the database. Runs before derivatives are built
    /// because comic renditions depend on the colour mode.
    /// </summary>
    private static void PreserveUserEdits(Book discovered, Book existing)
    {
        discovered.Subtitle = existing.Subtitle;
        discovered.File.IsAbridged = existing.File.IsAbridged;

        if (discovered.File.MediaType == MediaType.Ebook)
        {
            // EPUB provides all Dublin Core fields — nothing extra to preserve
        }
        else if (discovered.File.MediaType == MediaType.Audiobook)
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
        else if (discovered.File.MediaType == MediaType.Comic)
        {
            discovered.Rights = existing.Rights;
            discovered.Source = existing.Source;
            discovered.Type = existing.Type;
            discovered.Relation = existing.Relation;
            discovered.Coverage = existing.Coverage;
            discovered.Format = existing.Format;
            discovered.Identifiers = existing.Identifiers;
            discovered.File.ColorMode = existing.File.ColorMode ?? ComicColorMode.Auto;
        }
    }

    private static void ComputePageCount(Book book, int wordsPerPage)
    {
        if (book.File.WordCount is not null)
            book.File.PageCount = (int)Math.Ceiling((double)book.File.WordCount.Value / wordsPerPage);
    }

    private async Task HandleStaleBooks(List<Book> existingBooks, HashSet<Guid> claimedUuids)
    {
        var staleBooks = existingBooks.Where(b => !claimedUuids.Contains(b.BookUuid)).ToList();
        foreach (var stale in staleBooks)
        {
            logger.LogInformation("Removing stale entry: {Title} ({SourcePath})", stale.Title, stale.File.SourcePath);
            await bookRepository.Remove(stale.BookUuid);
        }

        if (staleBooks.Count > 0)
        {
            await authorRepository.RemoveOrphanedAuthors();
            await seriesRepository.RemoveOrphanedSeries();
        }
    }
}
