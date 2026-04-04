using Alexandria.Business.Extensions;
using Alexandria.Business.Helpers;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class AlexandriaLibraryService(IBookRepository bookRepository, IReadingStateRepository readingStateRepository, IBookCollectionRepository collectionRepository, IFileRepository fileRepository, IEpubMetadataHandler epubMetadataHandler, ILibraryIndexerService libraryIndexerService) : IAlexandriaLibraryService
{
    public async Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null)
    {
        var books = await bookRepository.GetAll();
        foreach (var book in books)
        {
            book.ReadingProgress = await readingStateRepository.GetReadingProgress(book.Uuid);
        }
        return AlexandriaLibraryHelper.SortBooks(books, sortOptions ?? new BookSortOptions());
    }

    public async Task<Book?> GetBookByUuid(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        book?.ReadingProgress = await readingStateRepository.GetReadingProgress(book.Uuid);
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
        var books = await GetBooks();
        var authors = books.SelectMany(b => b.Authors, (book, author) => new { Author = author, Book = book }).GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase).Select(g => new Author { Name = g.Key, Books = g.Select(x => x.Book).OrderBy(b => b.Title).ToList() }).ToList();
        return AlexandriaLibraryHelper.SortAuthors(authors, sortOptions ?? new AuthorSortOptions());
    }

    public async Task<Author?> GetAuthorByName(string name, BookSortOptions? sortOptions = null)
    {
        var books = await GetBooks();
        var authorBooks = books.Where(b => b.Authors.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (authorBooks.Count == 0)
        {
            return null;
        }
        return new Author { Name = name, Books = AlexandriaLibraryHelper.SortBooks(authorBooks, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc)) };
    }

    public async Task<List<Series>> GetSeries(SeriesSortOptions? sortOptions = null)
    {
        var books = await GetBooks();
        var series = books.Where(b => b.SeriesName is not null).GroupBy(b => b.SeriesName!).Select(g => new Series { Uuid = Guid.GenerateUuidV5(g.Key), Name = g.Key, Books = g.OrderBy(b => b.SeriesIndex ?? 0).ToList() }).ToList();
        return AlexandriaLibraryHelper.SortSeries(series, sortOptions ?? new SeriesSortOptions());
    }

    public async Task<Series?> GetSeriesByUuid(Guid uuid, BookSortOptions? sortOptions = null)
    {
        var allSeries = await GetSeries();
        var series = allSeries.FirstOrDefault(s => s.Uuid == uuid);
        if (series is not null)
        {
            series.Books = AlexandriaLibraryHelper.SortBooks(series.Books, sortOptions ?? new BookSortOptions(BookSortField.BookNumber, SortOrder.Asc));
        }
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

    public async Task<BookCollection?> GetCollectionByUuid(Guid uuid) => await collectionRepository.FindByUuid(uuid);

    public async Task<CollectionWithBooks?> GetCollectionWithBooks(Guid uuid, BookSortOptions? sortOptions = null)
    {
        var collection = await collectionRepository.FindByUuid(uuid);
        if (collection is null)
        {
            return null;
        }
        var allBooks = await GetBooks();
        var uuidSet = new HashSet<Guid>(collection.BookUuids);
        var books = allBooks.Where(b => uuidSet.Contains(b.Uuid)).ToList();
        return new CollectionWithBooks { Uuid = collection.Uuid, Name = collection.Name, Books = AlexandriaLibraryHelper.SortBooks(books, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc)), CreatedAt = collection.CreatedAt, LastModified = collection.LastModified };
    }

    public async Task<BookCollection> CreateOrUpdateCollection(string name, List<Guid> bookUuids)
    {
        var uuid = Guid.GenerateUuidV5(name.ToLowerInvariant().Trim());
        var existing = await collectionRepository.FindByUuid(uuid);
        var now = DateTimeOffset.UtcNow;

        var collection = new BookCollection
        {
            Uuid = uuid,
            Name = name,
            BookUuids = bookUuids,
            CreatedAt = existing?.CreatedAt ?? now,
            LastModified = now
        };

        await collectionRepository.Upsert(collection);
        return collection;
    }

    public async Task UpsertCollection(BookCollection collection) => await collectionRepository.Upsert(collection);

    public async Task DeleteCollection(Guid uuid) => await collectionRepository.Delete(uuid);

    public async Task<List<BookCollection>> GetCollectionsModifiedSince(DateTimeOffset since) => await collectionRepository.GetModifiedSince(since);

    public async Task UpdateReadingStatus(Guid uuid, string status)
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
                Status = status,
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
                ProgressPercent = status == "Finished" ? 100 : status == "ReadyToRead" ? 0 : existing?.CurrentBookmark.ProgressPercent,
                ContentSourceProgressPercent = status == "ReadyToRead" ? 0 : existing?.CurrentBookmark.ContentSourceProgressPercent,
                Location = status == "ReadyToRead" ? null : existing?.CurrentBookmark.Location
            }
        };

        await readingStateRepository.Upsert(uuid, state, "{}");
    }

    public async Task<Book?> UpdateBookMetadata(Guid uuid, Book book)
    {
        var existing = await bookRepository.FindByUuid(uuid);
        if (existing is null)
        {
            return null;
        }

        var metadata = book.ToEpubMetadata();
        epubMetadataHandler.EmbedMetadata(existing.SourcePath, metadata);

        await libraryIndexerService.ReindexBook(uuid);

        return await bookRepository.FindByUuid(uuid);
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
}
