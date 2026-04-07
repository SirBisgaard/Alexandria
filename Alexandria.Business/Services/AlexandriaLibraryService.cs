using Alexandria.Business.Extensions;
using Alexandria.Business.Helpers;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class AlexandriaLibraryService(IBookRepository bookRepository, IReadingStateRepository readingStateRepository, ICollectionRepository collectionRepository, IFileRepository fileRepository, IEpubMetadataHandler epubMetadataHandler, ILibraryIndexerService libraryIndexerService, IAuthorRepository authorRepository, ISeriesRepository seriesRepository) : IAlexandriaLibraryService
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
        return await fileRepository.ReadCover(book, width, height);
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
        return new CollectionWithBooks { CollectionUuid = collection.CollectionUuid, Name = collection.Name, Books = AlexandriaLibraryHelper.SortBooks(books, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc)), CreatedAt = collection.CreatedAt, LastModified = collection.LastModified };
    }

    public async Task UpsertCollection(BookCollection collection) => await collectionRepository.Upsert(collection);

    public async Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since) => await collectionRepository.GetModifiedSince(since);

    public async Task UpdateReadingStatus(Guid uuid, ReadingStatus status)
    {
        var existing = await readingStateRepository.GetByBookUuid(uuid);
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var state = new Models.KoboStore.ReadingState
        {
            EntitlementId = uuid.ToString(),
            Created = existing?.Created ?? now,
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new Models.KoboStore.StatusInfo
            {
                LastModified = now,
                Status = status.ToString(),
                TimesStartedReading = existing?.StatusInfo.TimesStartedReading ?? 0,
                LastTimeStartedReading = existing?.StatusInfo.LastTimeStartedReading
            },
            Statistics = new Models.KoboStore.Statistics
            {
                LastModified = now,
                SpentReadingMinutes = existing?.Statistics.SpentReadingMinutes,
                RemainingTimeMinutes = existing?.Statistics.RemainingTimeMinutes
            },
            CurrentBookmark = new Models.KoboStore.Bookmark
            {
                LastModified = now,
                ProgressPercent = status == ReadingStatus.Finished ? 100 : status == ReadingStatus.ReadyToRead ? 0 : existing?.CurrentBookmark.ProgressPercent,
                ContentSourceProgressPercent = status == ReadingStatus.ReadyToRead ? 0 : existing?.CurrentBookmark.ContentSourceProgressPercent,
                Location = status == ReadingStatus.ReadyToRead ? null : existing?.CurrentBookmark.Location
            }
        };

        await readingStateRepository.Upsert(uuid, state, "{}");
    }

    public async Task<Book?> UpdateBookMetadata(Book book)
    {
        var existing = await bookRepository.FindByUuid(book.BookUuid);
        if (existing is null)
        {
            return null;
        }

        var metadata = book.ToEpubMetadata();
        metadata.AlexandriaUuid = existing.BookUuid;
        epubMetadataHandler.EmbedMetadata(existing.SourcePath, metadata);

        await libraryIndexerService.ReindexBook(book.BookUuid);

        return await bookRepository.FindByUuid(book.BookUuid);
    }

    public async Task<Book?> UpdateBookCover(Guid uuid, byte[] coverData, string mediaType)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
        {
            return null;
        }

        epubMetadataHandler.EmbedCover(book.SourcePath, coverData, mediaType);

        await libraryIndexerService.ReindexBook(uuid);
        return await bookRepository.FindByUuid(uuid);
    }

    public async Task<Book?> RemoveBookCover(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
        {
            return null;
        }

        epubMetadataHandler.RemoveCover(book.SourcePath);

        await libraryIndexerService.ReindexBook(uuid);
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
        {
            var metadata = book.ToEpubMetadata();
            metadata.AlexandriaUuid = book.BookUuid;
            epubMetadataHandler.EmbedMetadata(book.SourcePath, metadata);
            await libraryIndexerService.ReindexBook(book.BookUuid);
        }

        return await GetAuthorByUuid(author.AuthorUuid);
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
        {
            var metadata = book.ToEpubMetadata();
            metadata.AlexandriaUuid = book.BookUuid;
            epubMetadataHandler.EmbedMetadata(book.SourcePath, metadata);
            await libraryIndexerService.ReindexBook(book.BookUuid);
        }

        return await GetSeriesByUuid(series.SeriesUuid);
    }

    public async Task<Series?> GetSeriesByName(string name)
    {
        return await seriesRepository.FindByName(name);
    }
}
