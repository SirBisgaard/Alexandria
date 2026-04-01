using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class BookRepository(AlexandriaSettings settings) : IBookRepository
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

        return books;
    }

    public async Task<Book?> FindByUuid(Guid uuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Books WHERE Uuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", uuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapBook(reader);
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
            return MapBook(reader);
        }

        return null;
    }

    public async Task Add(Book book)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Books (Uuid, FileHash, SourcePath, Title, Authors, Language, PublicationDate, Description, Publisher, SeriesName, SeriesIndex, CoverFilePath, KepubFilePath, KepubFileSize, CreatedAt, LastModified, IsArchived)
            VALUES (@uuid, @fileHash, @sourcePath, @title, @authors, @language, @publicationDate, @description, @publisher, @seriesName, @seriesIndex, @coverFilePath, @kepubFilePath, @kepubFileSize, @createdAt, @lastModified, @isArchived)
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
            UPDATE Books SET FileHash = @fileHash, SourcePath = @sourcePath, Title = @title, Authors = @authors, Language = @language, PublicationDate = @publicationDate, Description = @description, Publisher = @publisher, SeriesName = @seriesName, SeriesIndex = @seriesIndex, CoverFilePath = @coverFilePath, KepubFilePath = @kepubFilePath, KepubFileSize = @kepubFileSize, CreatedAt = @createdAt, LastModified = @lastModified, IsArchived = @isArchived
            WHERE Uuid = @uuid
            """;
        AddBookParameters(cmd, book);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task Remove(Guid uuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Books WHERE Uuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", uuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddBookParameters(SqliteCommand cmd, Book book)
    {
        cmd.Parameters.AddWithValue("@uuid", book.Uuid.ToString());
        cmd.Parameters.AddWithValue("@fileHash", book.FileHash);
        cmd.Parameters.AddWithValue("@sourcePath", book.SourcePath);
        cmd.Parameters.AddWithValue("@title", book.Title);
        cmd.Parameters.AddWithValue("@authors", JsonSerializer.Serialize(book.Authors));
        cmd.Parameters.AddWithValue("@language", book.Language);
        cmd.Parameters.AddWithValue("@publicationDate", (object?)book.PublicationDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@description", (object?)book.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@publisher", (object?)book.Publisher ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@seriesName", (object?)book.SeriesName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@seriesIndex", (object?)book.SeriesIndex ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@coverFilePath", (object?)book.CoverFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kepubFilePath", (object?)book.KepubFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kepubFileSize", book.KepubFileSize);
        cmd.Parameters.AddWithValue("@createdAt", book.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lastModified", book.LastModified.ToString("O"));
        cmd.Parameters.AddWithValue("@isArchived", book.IsArchived ? 1 : 0);
    }

    private static Book MapBook(SqliteDataReader reader)
    {
        return new Book
        {
            Uuid = Guid.Parse(reader.GetString(reader.GetOrdinal("Uuid"))),
            FileHash = reader.GetString(reader.GetOrdinal("FileHash")),
            SourcePath = reader.GetString(reader.GetOrdinal("SourcePath")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Authors = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("Authors"))) ?? [],
            Language = reader.GetString(reader.GetOrdinal("Language")),
            PublicationDate = reader.IsDBNull(reader.GetOrdinal("PublicationDate")) ? null : reader.GetString(reader.GetOrdinal("PublicationDate")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
            Publisher = reader.IsDBNull(reader.GetOrdinal("Publisher")) ? null : reader.GetString(reader.GetOrdinal("Publisher")),
            SeriesName = reader.IsDBNull(reader.GetOrdinal("SeriesName")) ? null : reader.GetString(reader.GetOrdinal("SeriesName")),
            SeriesIndex = reader.IsDBNull(reader.GetOrdinal("SeriesIndex")) ? null : reader.GetInt32(reader.GetOrdinal("SeriesIndex")),
            CoverFilePath = reader.IsDBNull(reader.GetOrdinal("CoverFilePath")) ? null : reader.GetString(reader.GetOrdinal("CoverFilePath")),
            KepubFilePath = reader.IsDBNull(reader.GetOrdinal("KepubFilePath")) ? null : reader.GetString(reader.GetOrdinal("KepubFilePath")),
            KepubFileSize = reader.GetInt64(reader.GetOrdinal("KepubFileSize")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            LastModified = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LastModified"))),
            IsArchived = reader.GetInt32(reader.GetOrdinal("IsArchived")) == 1
        };
    }
}
