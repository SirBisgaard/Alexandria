using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;

namespace Alexandria.Business.Services;

public class AlexandriaLibraryService(IBookRepository bookRepository, IReadingStateRepository readingStateRepository, IBookCollectionRepository collectionRepository, IFileRepository fileRepository) : IAlexandriaLibraryService
{
    public async Task<List<Book>> GetBooks()
    {
        var books = await bookRepository.GetAll();
        foreach (var book in books)
        {
            book.ReadingProgress = await readingStateRepository.GetReadingProgress(book.Uuid);
        }
        return books;
    }

    public async Task<Book?> GetBookByUuid(Guid uuid)
    {
        return await bookRepository.FindByUuid(uuid);
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

    public async Task<List<Author>> GetAuthors()
    {
        var books = await GetBooks();
        return books.SelectMany(b => b.Authors, (book, author) => new { Author = author, Book = book }).GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase).Select(g => new Author { Name = g.Key, Books = g.Select(x => x.Book).OrderBy(b => b.Title).ToList() }).OrderBy(a => a.Name).ToList();
    }

    public async Task<Author?> GetAuthorByName(string name)
    {
        var books = await GetBooks();
        var authorBooks = books.Where(b => b.Authors.Contains(name, StringComparer.OrdinalIgnoreCase)).OrderBy(b => b.Title).ToList();
        if (authorBooks.Count == 0)
        {
            return null;
        }
        return new Author { Name = name, Books = authorBooks };
    }

    public async Task<List<Series>> GetSeries()
    {
        var books = await GetBooks();
        return books.Where(b => b.SeriesName is not null).GroupBy(b => b.SeriesName!).Select(g => new Series { Uuid = Guid.GenerateUuidV5(g.Key), Name = g.Key, Books = g.OrderBy(b => b.SeriesIndex ?? 0).ToList() }).OrderBy(s => s.Name).ToList();
    }

    public async Task<Series?> GetSeriesByUuid(Guid uuid)
    {
        var allSeries = await GetSeries();
        return allSeries.FirstOrDefault(s => s.Uuid == uuid);
    }

    public async Task<byte[]?> GetBookCover(Guid uuid)
    {
        var book = await bookRepository.FindByUuid(uuid);
        if (book is null)
        {
            return null;
        }
        return await fileRepository.ReadCover(book);
    }

    public async Task<List<BookCollection>> GetCollections() => await collectionRepository.GetAll();

    public async Task<BookCollection?> GetCollectionByUuid(Guid uuid) => await collectionRepository.FindByUuid(uuid);

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
}
