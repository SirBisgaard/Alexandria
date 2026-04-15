using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Helpers;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Services;

public class AuthorService(IAuthorRepository authorRepository, IBookService bookService, IIndexerService indexerService) : IAuthorService
{
    public async Task<List<Author>> GetAuthors(AuthorSortOptions? sortOptions = null)
    {
        var authors = await authorRepository.GetAll();
        var books = await bookService.GetBooks();
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

        var books = await bookService.GetBooks();
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
        var books = await bookService.GetBooks();
        author.Books = books.Where(b => b.Authors.Any(a => a.AuthorUuid == author.AuthorUuid)).ToList();
        author.Books = AlexandriaLibraryHelper.SortBooks(author.Books, sortOptions ?? new BookSortOptions(BookSortField.Title, SortOrder.Asc));
        return author;
    }

    public async Task<Author?> UpdateAuthor(Author author)
    {
        var existing = await authorRepository.FindByUuid(author.AuthorUuid);
        if (existing is null)
            return null;

        await authorRepository.Update(author);

        var books = await bookService.GetBooks();
        var authorBooks = books.Where(b => b.Authors.Any(a => a.AuthorUuid == author.AuthorUuid)).ToList();

        foreach (var book in authorBooks)
            await indexerService.ReindexBook(book.BookUuid, book);

        return await GetAuthorByUuid(author.AuthorUuid);
    }

    public async Task<Author?> MergeAuthor(Guid sourceUuid, Guid targetUuid)
    {
        var books = await bookService.GetBooks();
        var affectedBooks = books.Where(b => b.Authors.Any(a => a.AuthorUuid == sourceUuid)).ToList();

        await authorRepository.ReassignBooks(sourceUuid, targetUuid);
        await authorRepository.RemoveOrphanedAuthors();

        books = await bookService.GetBooks();
        foreach (var book in books.Where(b => affectedBooks.Any(ab => b.BookUuid == ab.BookUuid)))
            await indexerService.ReindexBook(book.BookUuid, book);

        return await GetAuthorByUuid(targetUuid);
    }
}
