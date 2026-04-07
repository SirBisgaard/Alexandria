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
            await LoadSubTableData(connection, book);
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
            await LoadSubTableData(connection, book);
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
            await LoadSubTableData(connection, book);
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
            await LoadSubTableData(connection, book);
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
            await LoadSubTableData(connection, book);
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
            INSERT INTO Books (BookUuid, EntitlementUuid, FileHash, SourcePath, Title, Language, PublicationDate, Description, Publisher, CoverFilePath, KepubFilePath, KepubFileSize, CoverImageUuid, CreatedAt, ModifiedAt, IsArchived, Rights, Source, Type, Relation, Coverage, Format)
            VALUES (@bookUuid, @entitlementUuid, @fileHash, @sourcePath, @title, @language, @publicationDate, @description, @publisher, @coverFilePath, @kepubFilePath, @kepubFileSize, @coverImageUuid, @createdAt, @modifiedAt, @isArchived, @rights, @source, @type, @relation, @coverage, @format)
            """;
        AddBookParameters(cmd, book);
        await cmd.ExecuteNonQueryAsync();
        await SaveSubTableData(connection, book);
    }

    public async Task Update(Book book)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Books SET EntitlementUuid = @entitlementUuid, FileHash = @fileHash, SourcePath = @sourcePath, Title = @title, Language = @language, PublicationDate = @publicationDate, Description = @description, Publisher = @publisher, CoverFilePath = @coverFilePath, KepubFilePath = @kepubFilePath, KepubFileSize = @kepubFileSize, CoverImageUuid = @coverImageUuid, CreatedAt = @createdAt, ModifiedAt = @modifiedAt, IsArchived = @isArchived, Rights = @rights, Source = @source, Type = @type, Relation = @relation, Coverage = @coverage, Format = @format
            WHERE BookUuid = @bookUuid
            """;
        AddBookParameters(cmd, book);
        await cmd.ExecuteNonQueryAsync();
        await DeleteSubTableData(connection, book.BookUuid);
        await SaveSubTableData(connection, book);
    }

    public async Task Remove(Guid uuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await DeleteSubTableData(connection, uuid);

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
        cmd.Parameters.AddWithValue("@rights", (object?)book.Rights ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", (object?)book.Source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", (object?)book.Type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@relation", (object?)book.Relation ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@coverage", (object?)book.Coverage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@format", (object?)book.Format ?? DBNull.Value);
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
            IsArchived = reader.GetInt32(reader.GetOrdinal("IsArchived")) == 1,
            Rights = reader.IsDBNull(reader.GetOrdinal("Rights")) ? null : reader.GetString(reader.GetOrdinal("Rights")),
            Source = reader.IsDBNull(reader.GetOrdinal("Source")) ? null : reader.GetString(reader.GetOrdinal("Source")),
            Type = reader.IsDBNull(reader.GetOrdinal("Type")) ? null : reader.GetString(reader.GetOrdinal("Type")),
            Relation = reader.IsDBNull(reader.GetOrdinal("Relation")) ? null : reader.GetString(reader.GetOrdinal("Relation")),
            Coverage = reader.IsDBNull(reader.GetOrdinal("Coverage")) ? null : reader.GetString(reader.GetOrdinal("Coverage")),
            Format = reader.IsDBNull(reader.GetOrdinal("Format")) ? null : reader.GetString(reader.GetOrdinal("Format"))
        };
    }

    private static async Task LoadSubTableData(SqliteConnection connection, Book book)
    {
        book.Identifiers = await LoadSubTable(connection, "BookIdentifiers", book.BookUuid);
        book.Subjects = await LoadSubTable(connection, "BookSubjects", book.BookUuid);
        book.Contributors = await LoadSubTable(connection, "BookContributors", book.BookUuid);
    }

    private static async Task<List<string>> LoadSubTable(SqliteConnection connection, string tableName, Guid bookUuid)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT Value FROM {tableName} WHERE BookUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }

    private static async Task SaveSubTableData(SqliteConnection connection, Book book)
    {
        await SaveSubTable(connection, "BookIdentifiers", book.BookUuid, book.Identifiers);
        await SaveSubTable(connection, "BookSubjects", book.BookUuid, book.Subjects);
        await SaveSubTable(connection, "BookContributors", book.BookUuid, book.Contributors);
    }

    private static async Task SaveSubTable(SqliteConnection connection, string tableName, Guid bookUuid, List<string> values)
    {
        foreach (var value in values)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"INSERT OR IGNORE INTO {tableName} (BookUuid, Value) VALUES (@uuid, @value)";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@value", value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task DeleteSubTableData(SqliteConnection connection, Guid bookUuid)
    {
        foreach (var table in new[] { "BookIdentifiers", "BookSubjects", "BookContributors" })
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table} WHERE BookUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
