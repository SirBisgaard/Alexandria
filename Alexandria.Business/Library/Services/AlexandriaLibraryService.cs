using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Helpers;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Services;

public class AlexandriaLibraryService(IBookRepository bookRepository, IReadingStateRepository readingStateRepository, ICollectionRepository collectionRepository, IFileContentManager contentManager, ILibraryIndexerService libraryIndexerService, IAuthorRepository authorRepository, ISeriesRepository seriesRepository) : IAlexandriaLibraryService
{
    public async Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null)
    {
        var books = await bookRepository.GetAll();
        foreach (var book in books)
        {
            book.ReadingProgress = await readingStateRepository.GetReadingProgress(book.BookUuid);
        }
        return AlexandriaLibraryHelper.SortBooks(books, sortOptions ?? new BookSortOptions());
    }

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

    public async Task<Book?> GetBookByCoverImageUuid(Guid uuid)
    {
        var book = await bookRepository.FindByCoverImageUuid(uuid);
        if (book is not null)
        {
            book.ReadingProgress = await readingStateRepository.GetReadingProgress(book.BookUuid);
        }
        return book;
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

    public async Task<List<Author>> GetAuthors(AuthorSortOptions? sortOptions = null)
    {
        var authors = await authorRepository.GetAll();
        var books = await GetBooks();
        foreach (var author in authors)
        {
            author.Books = books.Where(b => b.Authors.Any(a => a.AuthorUuid == author.AuthorUuid)).OrderBy(b => b.Title).ToList();
        }
        var populatedAuthors = authors.Where(a => a.Books.Count > 0).ToList();
        return AlexandriaLibraryHelper.SortAuthors(populatedAuthors, sortOptions ?? new AuthorSortOptions());
    }

    public async Task<Author?> GetAuthorByUuid(Guid uuid, BookSortOptions? sortOptions = null)
    {
        var author = await authorRepository.FindByUuid(uuid);
        if (author is null)
            return null;

        var books = await GetBooks();
        author.Books = books.Where(b => b.Authors.Any(a => a.AuthorUuid == author.AuthorUuid)).ToList();
        author.Books = AlexandriaLibraryHelper.SortBooks(author.Books, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc));
        return author;
    }

    public async Task<Author?> GetAuthorByName(string name, BookSortOptions? sortOptions = null)
    {
        var author = await authorRepository.FindByName(name);
        if (author is null)
        {
            return null;
        }
        var books = await GetBooks();
        author.Books = books.Where(b => b.Authors.Any(a => a.AuthorUuid == author.AuthorUuid)).ToList();
        author.Books = AlexandriaLibraryHelper.SortBooks(author.Books, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc));
        return author;
    }

    public async Task<List<Series>> GetSeries(SeriesSortOptions? sortOptions = null)
    {
        var allSeries = await seriesRepository.GetAll();
        var books = await GetBooks();
        foreach (var series in allSeries)
        {
            series.Books = books.Where(b => b.Series?.SeriesUuid == series.SeriesUuid).OrderBy(b => b.Series?.Index ?? 0).ToList();
        }
        var populatedSeries = allSeries.Where(s => s.Books.Count > 0).ToList();
        return AlexandriaLibraryHelper.SortSeries(populatedSeries, sortOptions ?? new SeriesSortOptions());
    }

    public async Task<Series?> GetSeriesByUuid(Guid uuid, BookSortOptions? sortOptions = null)
    {
        var series = await seriesRepository.FindByUuid(uuid);
        if (series is null)
        {
            return null;
        }
        var books = await GetBooks();
        series.Books = books.Where(b => b.Series?.SeriesUuid == series.SeriesUuid).ToList();
        series.Books = AlexandriaLibraryHelper.SortBooks(series.Books, sortOptions ?? new BookSortOptions(BookSortField.BookNumber, SortOrder.Asc));
        return series;
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

    public async Task<List<BookCollection>> GetCollections(CollectionSortOptions? sortOptions = null)
    {
        var collections = await collectionRepository.GetAll();
        return AlexandriaLibraryHelper.SortCollections(collections, sortOptions ?? new CollectionSortOptions());
    }

    public async Task<CollectionWithBooks?> GetCollectionWithBooks(Guid uuid, BookSortOptions? sortOptions = null)
    {
        var collection = await collectionRepository.FindByUuid(uuid);
        if (collection is null)
        {
            return null;
        }
        var allBooks = await GetBooks();
        var uuidSet = new HashSet<Guid>(collection.BookUuids);
        var books = allBooks.Where(b => uuidSet.Contains(b.BookUuid)).ToList();
        return new CollectionWithBooks { CollectionUuid = collection.CollectionUuid, Name = collection.Name, Books = AlexandriaLibraryHelper.SortBooks(books, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc)), CreatedAt = collection.CreatedAt, ModifiedAt = collection.ModifiedAt };
    }

    public async Task UpsertCollection(BookCollection collection) => await collectionRepository.Upsert(collection);

    public async Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since) => await collectionRepository.GetModifiedSince(since);

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

        await libraryIndexerService.ReindexBook(book.BookUuid, book);
        return await bookRepository.FindByUuid(book.BookUuid);
    }

    public async Task<Book?> UpdateBookCover(Guid uuid, byte[] coverData, string mediaType)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
            return null;

        await libraryIndexerService.ReindexBook(uuid, coverUpdate: new CoverUpdate(coverData, mediaType));
        return await bookRepository.FindByUuid(uuid);
    }

    public async Task<Book?> RemoveBookCover(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
            return null;

        await libraryIndexerService.ReindexBook(uuid, coverUpdate: new CoverUpdate(RemoveCover: true));
        return await bookRepository.FindByUuid(uuid);
    }

    public async Task<int> Sync(IProgress<(int current, int total)>? progress = null)
    {
        return await libraryIndexerService.Index(progress);
    }

    public async Task<Author?> UpdateAuthor(Author author)
    {
        var existing = await authorRepository.FindByUuid(author.AuthorUuid);
        if (existing is null)
            return null;

        await authorRepository.Update(author);

        var books = await GetBooks();
        var authorBooks = books.Where(b => b.Authors.Any(a => a.AuthorUuid == author.AuthorUuid)).ToList();

        foreach (var book in authorBooks)
            await libraryIndexerService.ReindexBook(book.BookUuid, book);

        return await GetAuthorByUuid(author.AuthorUuid);
    }

    public async Task<Author?> MergeAuthor(Guid sourceUuid, Guid targetUuid)
    {
        await authorRepository.ReassignBooks(sourceUuid, targetUuid);
        await authorRepository.RemoveOrphanedAuthors();

        var books = await GetBooks();
        var affectedBooks = books.Where(b => b.Authors.Any(a => a.AuthorUuid == targetUuid)).ToList();
        foreach (var book in affectedBooks)
            await libraryIndexerService.ReindexBook(book.BookUuid, book);

        return await GetAuthorByUuid(targetUuid);
    }

    public async Task<Series?> UpdateSeries(Series series)
    {
        var existing = await seriesRepository.FindByUuid(series.SeriesUuid);
        if (existing is null)
            return null;

        await seriesRepository.Update(series);

        var books = await GetBooks();
        var seriesBooks = books.Where(b => b.Series?.SeriesUuid == series.SeriesUuid).ToList();

        foreach (var book in seriesBooks)
            await libraryIndexerService.ReindexBook(book.BookUuid, book);

        return await GetSeriesByUuid(series.SeriesUuid);
    }

    public async Task<Series?> MergeSeries(Guid sourceUuid, Guid targetUuid)
    {
        await seriesRepository.ReassignBooks(sourceUuid, targetUuid);
        await seriesRepository.RemoveOrphanedSeries();

        var books = await GetBooks();
        var affectedBooks = books.Where(b => b.Series?.SeriesUuid == targetUuid).ToList();
        foreach (var book in affectedBooks)
            await libraryIndexerService.ReindexBook(book.BookUuid, book);

        return await GetSeriesByUuid(targetUuid);
    }

    public async Task<Series?> GetSeriesByName(string name)
    {
        return await seriesRepository.FindByName(name);
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
        var candidates = allBooks.Where(b => b.BookUuid != bookUuid && !b.IsArchived && b.MediaType == book.MediaType).ToList();
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
