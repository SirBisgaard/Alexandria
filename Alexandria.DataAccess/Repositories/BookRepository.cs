using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class BookRepository(AlexandriaSettings settings, IAuthorRepository authorRepository, ISeriesRepository seriesRepository) : IBookRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task<List<Book>> GetAll()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Books";

        await using var reader = await cmd.ExecuteReaderAsync();
        var books = new List<Book>();
        while (await reader.ReadAsync())
        {
            books.Add(MapBook(reader));
        }

        foreach (var book in books)
        {
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
        }

        return books;
    }

    public async Task<Book?> FindByUuid(Guid uuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Books WHERE BookUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", uuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var book = MapBook(reader);
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
            return book;
        }

        return null;
    }

    public async Task<Book?> FindByEntitlementUuid(Guid entitlementUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Books WHERE EntitlementUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", entitlementUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var book = MapBook(reader);
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
            return book;
        }

        return null;
    }

    public async Task<Book?> FindByCoverImageUuid(Guid uuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Books WHERE CoverImageUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", uuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var book = MapBook(reader);
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
            return book;
        }

        return null;
    }

    public async Task<Book?> FindByHash(string fileHash)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Books WHERE FileHash = @hash";
        cmd.Parameters.AddWithValue("@hash", fileHash);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var book = MapBook(reader);
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
            return book;
        }

        return null;
    }

    public async Task Add(Book book)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Books (BookUuid, EntitlementUuid, FileHash, SourcePath, Title, Language, PublicationDate, Description, Publisher, CoverFilePath, KepubFilePath, KepubFileSize, CoverImageUuid, CreatedAt, ModifiedAt, IsArchived)
            VALUES (@bookUuid, @entitlementUuid, @fileHash, @sourcePath, @title, @language, @publicationDate, @description, @publisher, @coverFilePath, @kepubFilePath, @kepubFileSize, @coverImageUuid, @createdAt, @modifiedAt, @isArchived)
            """;
        AddBookParameters(cmd, book);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task Update(Book book)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Books SET EntitlementUuid = @entitlementUuid, FileHash = @fileHash, SourcePath = @sourcePath, Title = @title, Language = @language, PublicationDate = @publicationDate, Description = @description, Publisher = @publisher, CoverFilePath = @coverFilePath, KepubFilePath = @kepubFilePath, KepubFileSize = @kepubFileSize, CoverImageUuid = @coverImageUuid, CreatedAt = @createdAt, ModifiedAt = @modifiedAt, IsArchived = @isArchived
            WHERE BookUuid = @bookUuid
            """;
        AddBookParameters(cmd, book);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task Remove(Guid uuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var deleteBookSeries = connection.CreateCommand();
        deleteBookSeries.CommandText = "DELETE FROM BookSeries WHERE BookUuid = @uuid";
        deleteBookSeries.Parameters.AddWithValue("@uuid", uuid.ToString());
        await deleteBookSeries.ExecuteNonQueryAsync();

        await using var deleteBookAuthors = connection.CreateCommand();
        deleteBookAuthors.CommandText = "DELETE FROM BookAuthors WHERE BookUuid = @uuid";
        deleteBookAuthors.Parameters.AddWithValue("@uuid", uuid.ToString());
        await deleteBookAuthors.ExecuteNonQueryAsync();

        await using var deleteCollectionBooks = connection.CreateCommand();
        deleteCollectionBooks.CommandText = "DELETE FROM CollectionBooks WHERE BookUuid = @uuid";
        deleteCollectionBooks.Parameters.AddWithValue("@uuid", uuid.ToString());
        await deleteCollectionBooks.ExecuteNonQueryAsync();

        await using var deleteReadingStates = connection.CreateCommand();
        deleteReadingStates.CommandText = "DELETE FROM ReadingStates WHERE BookUuid = @uuid";
        deleteReadingStates.Parameters.AddWithValue("@uuid", uuid.ToString());
        await deleteReadingStates.ExecuteNonQueryAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Books WHERE BookUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", uuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddBookParameters(SqliteCommand cmd, Book book)
    {
        cmd.Parameters.AddWithValue("@bookUuid", book.BookUuid.ToString());
        cmd.Parameters.AddWithValue("@entitlementUuid", book.EntitlementUuid.ToString());
        cmd.Parameters.AddWithValue("@fileHash", book.FileHash);
        cmd.Parameters.AddWithValue("@sourcePath", book.SourcePath);
        cmd.Parameters.AddWithValue("@title", book.Title);
        cmd.Parameters.AddWithValue("@language", book.Language);
        cmd.Parameters.AddWithValue("@publicationDate", (object?)book.PublicationDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@description", (object?)book.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@publisher", (object?)book.Publisher ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@coverFilePath", (object?)book.CoverFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kepubFilePath", (object?)book.KepubFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kepubFileSize", book.KepubFileSize);
        cmd.Parameters.AddWithValue("@coverImageUuid", (object?)book.CoverImageUuid?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", book.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@modifiedAt", book.ModifiedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@isArchived", book.IsArchived ? 1 : 0);
    }

    private static Book MapBook(SqliteDataReader reader)
    {
        return new Book
        {
            BookUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("BookUuid"))),
            EntitlementUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("EntitlementUuid"))),
            FileHash = reader.GetString(reader.GetOrdinal("FileHash")),
            SourcePath = reader.GetString(reader.GetOrdinal("SourcePath")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Language = reader.GetString(reader.GetOrdinal("Language")),
            PublicationDate = reader.IsDBNull(reader.GetOrdinal("PublicationDate")) ? null : reader.GetString(reader.GetOrdinal("PublicationDate")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
            Publisher = reader.IsDBNull(reader.GetOrdinal("Publisher")) ? null : reader.GetString(reader.GetOrdinal("Publisher")),
            CoverFilePath = reader.IsDBNull(reader.GetOrdinal("CoverFilePath")) ? null : reader.GetString(reader.GetOrdinal("CoverFilePath")),
            KepubFilePath = reader.IsDBNull(reader.GetOrdinal("KepubFilePath")) ? null : reader.GetString(reader.GetOrdinal("KepubFilePath")),
            KepubFileSize = reader.GetInt64(reader.GetOrdinal("KepubFileSize")),
            CoverImageUuid = reader.IsDBNull(reader.GetOrdinal("CoverImageUuid")) ? null : Guid.Parse(reader.GetString(reader.GetOrdinal("CoverImageUuid"))),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ModifiedAt"))),
            IsArchived = reader.GetInt32(reader.GetOrdinal("IsArchived")) == 1
        };
    }
}
