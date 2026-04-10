using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Library.Services;

public class AlexandriaIndexerService(IFileContentManager contentManager, IBookRepository bookRepository, IAlexandriaSettingsService settingsService, IAuthorRepository authorRepository, ISeriesRepository seriesRepository, ILogger<AlexandriaIndexerService> logger) : ILibraryIndexerService
{
    private static readonly SemaphoreSlim IndexLock = new(1, 1);

    public async Task<int> Index(IProgress<(int current, int total)>? progress = null)
    {
        await IndexLock.WaitAsync();
        try
        {
            logger.LogInformation("Library indexing started.");

            var existingBooks = await bookRepository.GetAll();
            var existingByUuid = existingBooks.ToDictionary(b => b.BookUuid);

            var discoveredBooks = await contentManager.GetAllBooks(existingBooks, progress);
            var discoveredByUuid = discoveredBooks.ToDictionary(b => b.BookUuid);

            await HandleStaleBooks(existingBooks, discoveredByUuid);
            var wordsPerPage = (await settingsService.Get()).WordsPerPage;

            foreach (var discovered in discoveredBooks)
            {
                if (existingByUuid.TryGetValue(discovered.BookUuid, out var existing) && existing.FileHash == discovered.FileHash)
                    continue;

                var isNew = !existingByUuid.ContainsKey(discovered.BookUuid);
                if (isNew)
                    await HandleNewBook(discovered, wordsPerPage);
                else
                    await HandleExistingBook(discovered, existingByUuid[discovered.BookUuid], wordsPerPage);
            }

            await settingsService.UpdateLastIndexedTimestamp(DateTimeOffset.UtcNow);
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

            logger.LogInformation("Reindexing book: {Title}", existing.Title);

            if (desiredMetadata is not null || coverUpdate is not null)
            {
                var bookToUpdate = desiredMetadata ?? existing;
                bookToUpdate.SourcePath = existing.SourcePath;
                await contentManager.UpdateBook(bookToUpdate, coverUpdate);
            }

            var refreshed = await contentManager.GetBook(existing);
            refreshed.BookUuid = existing.BookUuid;
            refreshed.EntitlementUuid = existing.EntitlementUuid;
            refreshed.CoverImageUuid = Guid.GenerateUuidV5("cover:" + refreshed.FileHash);
            refreshed.CreatedAt = existing.CreatedAt;
            refreshed.ModifiedAt = DateTimeOffset.UtcNow;
            MergeDbOnlyFields(refreshed, desiredMetadata ?? existing);
            var wordsPerPage = (await settingsService.Get()).WordsPerPage;
            ComputePageCount(refreshed, wordsPerPage);

            await PersistBook(refreshed, false);

            logger.LogInformation("Reindexed: {Title} by {Authors}", refreshed.Title, string.Join(", ", refreshed.Authors.Select(a => a.Name)));
        }
        finally
        {
            IndexLock.Release();
        }
    }

    private async Task HandleNewBook(Book discovered, int wordsPerPage)
    {
        logger.LogInformation("Indexing new book: {Title}", discovered.Title);
        var now = DateTimeOffset.UtcNow;

        discovered.EntitlementUuid = discovered.GenerateDeterministicUuid();
        discovered.CoverImageUuid = Guid.GenerateUuidV5("cover:" + discovered.FileHash);
        discovered.CreatedAt = now;
        discovered.ModifiedAt = now;
        ComputePageCount(discovered, wordsPerPage);

        await PersistBook(discovered, true);

        logger.LogInformation("Indexed: {Title} by {Authors}", discovered.Title, string.Join(", ", discovered.Authors.Select(a => a.Name)));
    }

    private async Task HandleExistingBook(Book discovered, Book existing, int wordsPerPage)
    {
        logger.LogInformation("Updating changed book: {Title}", discovered.Title);

        discovered.EntitlementUuid = existing.EntitlementUuid;
        discovered.CoverImageUuid = Guid.GenerateUuidV5("cover:" + discovered.FileHash);
        discovered.CreatedAt = existing.CreatedAt;
        discovered.ModifiedAt = DateTimeOffset.UtcNow;
        MergeDbOnlyFields(discovered, existing);
        ComputePageCount(discovered, wordsPerPage);

        await PersistBook(discovered, false);

        logger.LogInformation("Updated: {Title} by {Authors}", discovered.Title, string.Join(", ", discovered.Authors.Select(a => a.Name)));
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

    private async Task HandleStaleBooks(List<Book> existingBooks, Dictionary<Guid, Book> discoveredByUuid)
    {
        var staleBooks = existingBooks.Where(b => !discoveredByUuid.ContainsKey(b.BookUuid)).ToList();
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
