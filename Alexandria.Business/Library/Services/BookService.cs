using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Helpers;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Services;

public class BookService(IBookRepository bookRepository, IReadingStateRepository readingStateRepository, IFileContentManager contentManager, IIndexerService indexerService) : IBookService
{
    public async Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null)
    {
        var books = await bookRepository.GetAll();
        var progressByBook = await readingStateRepository.GetAllReadingProgress();
        foreach (var book in books)
        {
            book.ReadingProgress = progressByBook.GetValueOrDefault(book.BookUuid);
        }
        return AlexandriaLibraryHelper.SortBooks(books, sortOptions ?? new BookSortOptions());
    }

    public Task<List<BookCoverKey>> GetCoverLookupKeys() => bookRepository.GetCoverLookupKeys();

    public async Task<Book?> GetBookByUuid(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is not null)
        {
            book.ReadingProgress = await readingStateRepository.GetReadingProgress(book.BookUuid);
        }
        return book;
    }

    public async Task<Book?> GetBookByEntitlementUuid(Guid entitlementUuid)
    {
        var book = await bookRepository.FindByEntitlementUuid(entitlementUuid);
        if (book is not null)
        {
            book.ReadingProgress = await readingStateRepository.GetReadingProgress(book.BookUuid);
        }
        return book;
    }

    public async Task<byte[]?> GetBookCover(Guid uuid, int? width = null, int? height = null)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
        {
            return null;
        }
        return await contentManager.GetCoverFile(book, width, height);
    }

    public async Task SetBookArchiveStatus(Guid uuid, bool isArchived)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
        {
            return;
        }
        book.IsArchived = isArchived;
        book.ModifiedAt = DateTimeOffset.UtcNow;
        await bookRepository.Update(book);
    }

    public async Task UpdateReadingStatus(Guid uuid, ReadingStatus status)
    {
        var existing = await readingStateRepository.GetByBookUuid(uuid);
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var state = new KoboStoreReadingState
        {
            EntitlementId = uuid.ToString(),
            Created = existing?.Created ?? now,
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo
            {
                LastModified = now,
                Status = status.ToString(),
                TimesStartedReading = existing?.StatusInfo.TimesStartedReading ?? 0,
                LastTimeStartedReading = existing?.StatusInfo.LastTimeStartedReading
            },
            Statistics = new KoboStoreStatistics
            {
                LastModified = now,
                SpentReadingMinutes = existing?.Statistics.SpentReadingMinutes,
                RemainingTimeMinutes = existing?.Statistics.RemainingTimeMinutes
            },
            CurrentBookmark = new KoboStoreBookmark
            {
                LastModified = now,
                ProgressPercent = status == ReadingStatus.Finished ? 100 : status == ReadingStatus.ReadyToRead ? 0 : existing?.CurrentBookmark.ProgressPercent,
                ContentSourceProgressPercent = status == ReadingStatus.ReadyToRead ? 0 : existing?.CurrentBookmark.ContentSourceProgressPercent,
                Location = status == ReadingStatus.ReadyToRead ? null : existing?.CurrentBookmark.Location
            }
        };

        await readingStateRepository.Upsert(uuid, state);

        var book = await bookRepository.FindByUuid(uuid);
        if (book is not null)
        {
            book.ModifiedAt = DateTimeOffset.UtcNow;
            await bookRepository.Update(book);
        }
    }

    public async Task<Book?> UpdateBookMetadata(Book book)
    {
        var existing = await bookRepository.FindByUuid(book.BookUuid);
        if (existing is null)
            return null;

        await indexerService.ReindexBook(book.BookUuid, book);
        return await bookRepository.FindByUuid(book.BookUuid);
    }

    public async Task<Book?> UpdateBookCover(Guid uuid, byte[] coverData, string mediaType)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
            return null;

        await indexerService.ReindexBook(uuid, coverUpdate: new CoverUpdate { CoverData = coverData, MediaType = mediaType });
        return await bookRepository.FindByUuid(uuid);
    }

    public async Task<Book?> RemoveBookCover(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
            return null;

        await indexerService.ReindexBook(uuid, coverUpdate: new CoverUpdate { RemoveCover = true });
        return await bookRepository.FindByUuid(uuid);
    }

    public async Task<List<string>> GetPublishers()
    {
        return await bookRepository.GetPublishers();
    }

    public async Task<List<Book>> GetRecommendations(Guid bookUuid, int count = 5)
    {
        var book = await bookRepository.FindByUuid(bookUuid);
        if (book is null)
            return [];

        var allBooks = await bookRepository.GetAll();
        var candidates = allBooks.Where(b => b.BookUuid != bookUuid && !b.IsArchived && b.File.MediaType == book.File.MediaType).ToList();
        var results = new List<Book>();
        var used = new HashSet<Guid>();

        // 1. Next books in the same series (by index, after the current book)
        if (book.Series is not null)
        {
            var seriesBooks = candidates
                .Where(b => b.Series?.SeriesUuid == book.Series.SeriesUuid && b.Series?.Index > (book.Series.Index ?? 0))
                .OrderBy(b => b.Series?.Index ?? 0);
            foreach (var b in seriesBooks)
            {
                if (results.Count >= count)
                    return results;
                if (used.Add(b.BookUuid))
                    results.Add(b);
            }
        }

        // 2. Books by the same author
        var authorUuids = book.Authors.Select(a => a.AuthorUuid).ToHashSet();
        var authorBooks = candidates
            .Where(b => b.Authors.Any(a => authorUuids.Contains(a.AuthorUuid)))
            .OrderBy(b => b.Title);
        foreach (var b in authorBooks)
        {
            if (results.Count >= count)
                return results;
            if (used.Add(b.BookUuid))
                results.Add(b);
        }

        // 3. Books in the same genre (shared subjects)
        if (book.Subjects.Count > 0)
        {
            var subjects = book.Subjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var genreBooks = candidates
                .Where(b => b.Subjects.Any(s => subjects.Contains(s)))
                .OrderByDescending(b => b.Subjects.Count(s => subjects.Contains(s)))
                .ThenBy(b => b.Title);
            foreach (var b in genreBooks)
            {
                if (results.Count >= count)
                    return results;
                if (used.Add(b.BookUuid))
                    results.Add(b);
            }
        }

        // 4. Random books from the library
        var random = candidates
            .Where(b => !used.Contains(b.BookUuid))
            .OrderBy(_ => Random.Shared.Next());
        foreach (var b in random)
        {
            if (results.Count >= count)
                return results;
            if (used.Add(b.BookUuid))
                results.Add(b);
        }

        return results;
    }
}
