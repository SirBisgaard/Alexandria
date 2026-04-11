using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

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

    public async Task<Book?> FindByPath(string sourcePath)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Books WHERE SourcePath = @path COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@path", sourcePath);

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
            INSERT INTO Books (BookUuid, EntitlementUuid, MediaType, SourcePath, Title, Subtitle, Language, Isbn, PublicationDate, Description, Publisher, CoverFilePath, KepubFilePath, TotalFileSize, PageCount, WordCount, Duration, IsAbridged, CreatedAt, ModifiedAt, IsArchived, Rights, Source, Type, Relation, Coverage, Format)
            VALUES (@bookUuid, @entitlementUuid, @mediaType, @sourcePath, @title, @subtitle, @language, @isbn, @publicationDate, @description, @publisher, @coverFilePath, @kepubFilePath, @totalFileSize, @pageCount, @wordCount, @duration, @isAbridged, @createdAt, @modifiedAt, @isArchived, @rights, @source, @type, @relation, @coverage, @format)
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
            UPDATE Books SET EntitlementUuid = @entitlementUuid, MediaType = @mediaType, SourcePath = @sourcePath, Title = @title, Subtitle = @subtitle, Language = @language, Isbn = @isbn, PublicationDate = @publicationDate, Description = @description, Publisher = @publisher, CoverFilePath = @coverFilePath, KepubFilePath = @kepubFilePath, TotalFileSize = @totalFileSize, PageCount = @pageCount, WordCount = @wordCount, Duration = @duration, IsAbridged = @isAbridged, CreatedAt = @createdAt, ModifiedAt = @modifiedAt, IsArchived = @isArchived, Rights = @rights, Source = @source, Type = @type, Relation = @relation, Coverage = @coverage, Format = @format
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
        deleteReadingStates.CommandText = "DELETE FROM KoboReadingStates WHERE BookUuid = @uuid";
        deleteReadingStates.Parameters.AddWithValue("@uuid", uuid.ToString());
        await deleteReadingStates.ExecuteNonQueryAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Books WHERE BookUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", uuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetPublishers()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT Publisher FROM Books WHERE Publisher IS NOT NULL AND Publisher != '' ORDER BY Publisher";

        await using var reader = await cmd.ExecuteReaderAsync();
        var publishers = new List<string>();
        while (await reader.ReadAsync())
            publishers.Add(reader.GetString(0));
        return publishers;
    }

    private static void AddBookParameters(SqliteCommand cmd, Book book)
    {
        cmd.Parameters.AddWithValue("@bookUuid", book.BookUuid.ToString());
        cmd.Parameters.AddWithValue("@entitlementUuid", book.EntitlementUuid.ToString());
        cmd.Parameters.AddWithValue("@mediaType", book.MediaType.ToString());
        cmd.Parameters.AddWithValue("@sourcePath", book.SourcePath);
        cmd.Parameters.AddWithValue("@title", book.Title);
        cmd.Parameters.AddWithValue("@subtitle", (object?)book.Subtitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@language", book.Language);
        cmd.Parameters.AddWithValue("@isbn", (object?)book.Isbn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@publicationDate", (object?)book.PublicationDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@description", (object?)book.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@publisher", (object?)book.Publisher ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@coverFilePath", (object?)book.CoverFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kepubFilePath", (object?)book.KepubFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@totalFileSize", book.TotalFileSize);
        cmd.Parameters.AddWithValue("@pageCount", (object?)book.PageCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wordCount", (object?)book.WordCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@duration", (object?)book.Duration ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isAbridged", book.IsAbridged.HasValue ? (book.IsAbridged.Value ? 1 : 0) : DBNull.Value);
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
            MediaType = Enum.Parse<MediaType>(reader.GetString(reader.GetOrdinal("MediaType"))),
            SourcePath = reader.GetString(reader.GetOrdinal("SourcePath")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Subtitle = reader.IsDBNull(reader.GetOrdinal("Subtitle")) ? null : reader.GetString(reader.GetOrdinal("Subtitle")),
            Language = reader.GetString(reader.GetOrdinal("Language")),
            Isbn = reader.IsDBNull(reader.GetOrdinal("Isbn")) ? null : reader.GetString(reader.GetOrdinal("Isbn")),
            PublicationDate = reader.IsDBNull(reader.GetOrdinal("PublicationDate")) ? null : reader.GetString(reader.GetOrdinal("PublicationDate")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
            Publisher = reader.IsDBNull(reader.GetOrdinal("Publisher")) ? null : reader.GetString(reader.GetOrdinal("Publisher")),
            CoverFilePath = reader.IsDBNull(reader.GetOrdinal("CoverFilePath")) ? null : reader.GetString(reader.GetOrdinal("CoverFilePath")),
            KepubFilePath = reader.IsDBNull(reader.GetOrdinal("KepubFilePath")) ? null : reader.GetString(reader.GetOrdinal("KepubFilePath")),
            TotalFileSize = reader.GetInt64(reader.GetOrdinal("TotalFileSize")),
            PageCount = reader.IsDBNull(reader.GetOrdinal("PageCount")) ? null : reader.GetInt32(reader.GetOrdinal("PageCount")),
            WordCount = reader.IsDBNull(reader.GetOrdinal("WordCount")) ? null : reader.GetInt32(reader.GetOrdinal("WordCount")),
            Duration = reader.IsDBNull(reader.GetOrdinal("Duration")) ? null : reader.GetInt32(reader.GetOrdinal("Duration")),
            IsAbridged = reader.IsDBNull(reader.GetOrdinal("IsAbridged")) ? null : reader.GetInt32(reader.GetOrdinal("IsAbridged")) == 1,
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
        book.Identifiers = await LoadStringSubTable(connection, "BookIdentifiers", book.BookUuid);
        book.Subjects = await LoadStringSubTable(connection, "BookSubjects", book.BookUuid);
        book.Contributors = await LoadContributors(connection, book.BookUuid);
        if (book.MediaType == MediaType.Audiobook)
            book.Chapters = await LoadChapters(connection, book.BookUuid);
    }

    private static async Task<List<string>> LoadStringSubTable(SqliteConnection connection, string tableName, Guid bookUuid)
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

    private static async Task<List<Contributor>> LoadContributors(SqliteConnection connection, Guid bookUuid)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name, Role FROM BookContributors WHERE BookUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        var contributors = new List<Contributor>();
        while (await reader.ReadAsync())
        {
            contributors.Add(new Contributor
            {
                Name = reader.GetString(0),
                Role = reader.IsDBNull(1) ? null : reader.GetString(1)
            });
        }
        return contributors;
    }

    private static async Task<List<AudiobookChapter>> LoadChapters(SqliteConnection connection, Guid bookUuid)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ChapterIndex, Title, Duration, StartTime, EndTime FROM AudiobookChapters WHERE BookUuid = @uuid ORDER BY ChapterIndex";
        cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        var chapters = new List<AudiobookChapter>();
        while (await reader.ReadAsync())
        {
            chapters.Add(new AudiobookChapter
            {
                ChapterIndex = reader.GetInt32(0),
                Title = reader.GetString(1),
                Duration = reader.GetDouble(2),
                StartTime = reader.GetDouble(3),
                EndTime = reader.GetDouble(4)
            });
        }
        return chapters;
    }

    private static async Task SaveSubTableData(SqliteConnection connection, Book book)
    {
        await SaveStringSubTable(connection, "BookIdentifiers", book.BookUuid, book.Identifiers);
        await SaveStringSubTable(connection, "BookSubjects", book.BookUuid, book.Subjects);
        await SaveContributors(connection, book.BookUuid, book.Contributors);
        if (book.Chapters.Count > 0)
            await SaveChapters(connection, book.BookUuid, book.Chapters);
    }

    private static async Task SaveStringSubTable(SqliteConnection connection, string tableName, Guid bookUuid, List<string> values)
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

    private static async Task SaveContributors(SqliteConnection connection, Guid bookUuid, List<Contributor> contributors)
    {
        foreach (var contributor in contributors)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO BookContributors (BookUuid, Name, Role) VALUES (@uuid, @name, @role)";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@name", contributor.Name);
            cmd.Parameters.AddWithValue("@role", (object?)contributor.Role ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task SaveChapters(SqliteConnection connection, Guid bookUuid, List<AudiobookChapter> chapters)
    {
        foreach (var chapter in chapters)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO AudiobookChapters (BookUuid, ChapterIndex, Title, Duration, StartTime, EndTime) VALUES (@uuid, @index, @title, @duration, @startTime, @endTime)";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@index", chapter.ChapterIndex);
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@duration", chapter.Duration);
            cmd.Parameters.AddWithValue("@startTime", chapter.StartTime);
            cmd.Parameters.AddWithValue("@endTime", chapter.EndTime);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task DeleteSubTableData(SqliteConnection connection, Guid bookUuid)
    {
        foreach (var table in new[] { "BookIdentifiers", "BookSubjects", "BookContributors", "AudiobookChapters" })
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table} WHERE BookUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
