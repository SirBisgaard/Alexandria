using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class AlexandriaLibraryService(IBookRepository bookRepository, IReadingStateRepository readingStateRepository, IBookCollectionRepository collectionRepository, IFileRepository fileRepository) : IAlexandriaLibraryService
{
    public async Task<List<Book>> GetBooks(BookSortOptions? sortOptions = null)
    {
        var books = await bookRepository.GetAll();
        foreach (var book in books)
        {
            book.ReadingProgress = await readingStateRepository.GetReadingProgress(book.Uuid);
        }
        return SortBooks(books, sortOptions ?? new BookSortOptions());
    }

    private static List<Book> SortBooks(List<Book> books, BookSortOptions options)
    {
        var desc = options.Order == SortOrder.Desc;
        return options.Sort switch
        {
            BookSortField.DateAdded => desc ? books.OrderByDescending(b => b.CreatedAt).ToList() : books.OrderBy(b => b.CreatedAt).ToList(),
            BookSortField.Title => desc ? books.OrderByDescending(b => b.Title).ToList() : books.OrderBy(b => b.Title).ToList(),
            BookSortField.Author => desc ? books.OrderByDescending(b => b.Authors.FirstOrDefault() ?? "").ThenByDescending(b => b.Title).ToList() : books.OrderBy(b => b.Authors.FirstOrDefault() ?? "").ThenBy(b => b.Title).ToList(),
            BookSortField.Series => desc ? books.OrderBy(b => b.SeriesName is null ? 1 : 0).ThenByDescending(b => b.SeriesName).ThenByDescending(b => b.SeriesIndex ?? 0).ToList() : books.OrderBy(b => b.SeriesName is null ? 1 : 0).ThenBy(b => b.SeriesName).ThenBy(b => b.SeriesIndex ?? 0).ToList(),
            BookSortField.FileSize => desc ? books.OrderByDescending(b => b.KepubFileSize).ToList() : books.OrderBy(b => b.KepubFileSize).ToList(),
            BookSortField.BookNumber => desc ? books.OrderByDescending(b => b.SeriesIndex ?? 0).ToList() : books.OrderBy(b => b.SeriesIndex ?? 0).ToList(),
            _ => desc ? books.OrderByDescending(b => b.LastModified).ToList() : books.OrderBy(b => b.LastModified).ToList()
        };
    }

    public async Task<Book?> GetBookByUuid(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        book?.ReadingProgress = await readingStateRepository.GetReadingProgress(book.Uuid);
        return book;
    }

    public async Task ArchiveBook(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
        {
            return;
        }
        book.IsArchived = true;
        await bookRepository.Update(book);
    }

    public async Task<List<Author>> GetAuthors(AuthorSortOptions? sortOptions = null)
    {
        var books = await GetBooks();
        var authors = books.SelectMany(b => b.Authors, (book, author) => new { Author = author, Book = book }).GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase).Select(g => new Author { Name = g.Key, Books = g.Select(x => x.Book).OrderBy(b => b.Title).ToList() }).ToList();
        return SortAuthors(authors, sortOptions ?? new AuthorSortOptions());
    }

    private static List<Author> SortAuthors(List<Author> authors, AuthorSortOptions options)
    {
        var desc = options.Order == SortOrder.Desc;
        return options.Sort switch
        {
            AuthorSortField.FirstName => desc ? authors.OrderByDescending(a => a.Name.Split(' ').First()).ToList() : authors.OrderBy(a => a.Name.Split(' ').First()).ToList(),
            AuthorSortField.Recent => desc ? authors.OrderByDescending(a => a.Books.Max(b => b.LastModified)).ToList() : authors.OrderBy(a => a.Books.Max(b => b.LastModified)).ToList(),
            AuthorSortField.NumberOfBooks => desc ? authors.OrderByDescending(a => a.Books.Count).ThenBy(a => a.Name).ToList() : authors.OrderBy(a => a.Books.Count).ThenBy(a => a.Name).ToList(),
            _ => desc ? authors.OrderByDescending(a => a.Name).ToList() : authors.OrderBy(a => a.Name).ToList()
        };
    }

    public async Task<Author?> GetAuthorByName(string name, BookSortOptions? sortOptions = null)
    {
        var books = await GetBooks();
        var authorBooks = books.Where(b => b.Authors.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (authorBooks.Count == 0)
        {
            return null;
        }
        return new Author { Name = name, Books = SortBooks(authorBooks, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc)) };
    }

    public async Task<List<Series>> GetSeries(SeriesSortOptions? sortOptions = null)
    {
        var books = await GetBooks();
        var series = books.Where(b => b.SeriesName is not null).GroupBy(b => b.SeriesName!).Select(g => new Series { Uuid = Guid.GenerateUuidV5(g.Key), Name = g.Key, Books = g.OrderBy(b => b.SeriesIndex ?? 0).ToList() }).ToList();
        return SortSeries(series, sortOptions ?? new SeriesSortOptions());
    }

    private static List<Series> SortSeries(List<Series> series, SeriesSortOptions options)
    {
        var desc = options.Order == SortOrder.Desc;
        return options.Sort switch
        {
            SeriesSortField.Recent => desc ? series.OrderByDescending(s => s.Books.Max(b => b.LastModified)).ToList() : series.OrderBy(s => s.Books.Max(b => b.LastModified)).ToList(),
            SeriesSortField.NumberOfBooks => desc ? series.OrderByDescending(s => s.Books.Count).ThenBy(s => s.Name).ToList() : series.OrderBy(s => s.Books.Count).ThenBy(s => s.Name).ToList(),
            _ => desc ? series.OrderByDescending(s => s.Name).ToList() : series.OrderBy(s => s.Name).ToList()
        };
    }

    public async Task<Series?> GetSeriesByUuid(Guid uuid, BookSortOptions? sortOptions = null)
    {
        var allSeries = await GetSeries();
        var series = allSeries.FirstOrDefault(s => s.Uuid == uuid);
        if (series is not null)
        {
            series.Books = SortBooks(series.Books, sortOptions ?? new BookSortOptions(BookSortField.BookNumber, SortOrder.Asc));
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
        return SortCollections(collections, sortOptions ?? new CollectionSortOptions());
    }

    private static List<BookCollection> SortCollections(List<BookCollection> collections, CollectionSortOptions options)
    {
        var desc = options.Order == SortOrder.Desc;
        return options.Sort switch
        {
            CollectionSortField.NumberOfBooks => desc ? collections.OrderByDescending(c => c.BookUuids.Count).ThenBy(c => c.Name).ToList() : collections.OrderBy(c => c.BookUuids.Count).ThenBy(c => c.Name).ToList(),
            CollectionSortField.DateCreated => desc ? collections.OrderByDescending(c => c.CreatedAt).ToList() : collections.OrderBy(c => c.CreatedAt).ToList(),
            _ => desc ? collections.OrderByDescending(c => c.Name).ToList() : collections.OrderBy(c => c.Name).ToList()
        };
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
        return new CollectionWithBooks { Uuid = collection.Uuid, Name = collection.Name, Books = SortBooks(books, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc)), CreatedAt = collection.CreatedAt, LastModified = collection.LastModified };
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
                RemainingTimeMinutes = status == "Finished" ? 0 : existing?.Statistics.RemainingTimeMinutes
            },
            CurrentBookmark = new Models.KoboStore.Bookmark
            {
                LastModified = now,
                ProgressPercent = status == "Finished" ? 100 : status == "ReadyToRead" ? 0 : existing?.CurrentBookmark.ProgressPercent,
                ContentSourceProgressPercent = status == "Finished" ? 100 : status == "ReadyToRead" ? 0 : existing?.CurrentBookmark.ContentSourceProgressPercent,
                Location = status == "ReadyToRead" ? null : existing?.CurrentBookmark.Location
            }
        };

        await readingStateRepository.Upsert(uuid, state, "{}");
    }
}
