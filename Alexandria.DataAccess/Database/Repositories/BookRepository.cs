using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class BookRepository(ISqliteConnectionProvider connectionProvider, IAuthorRepository authorRepository, ISeriesRepository seriesRepository) : IBookRepository
{
    public async Task<List<Book>> GetAll()
    {
        var books = await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM Books";

            using var reader = cmd.ExecuteReader();
            var result = new List<Book>();
            while (reader.Read())
            {
                result.Add(MapBook(reader));
            }

            foreach (var book in result)
                LoadSubTableData(connection, book);

            return result;
        });

        foreach (var book in books)
        {
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
        }

        return books;
    }

    public async Task<Book?> FindByUuid(Guid uuid)
    {
        var book = await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM Books WHERE BookUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", uuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var b = MapBook(reader);
                LoadSubTableData(connection, b);
                return b;
            }

            return (Book?)null;
        });

        if (book is not null)
        {
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
        }

        return book;
    }

    public async Task<Book?> FindByEntitlementUuid(Guid entitlementUuid)
    {
        var book = await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM Books WHERE EntitlementUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", entitlementUuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var b = MapBook(reader);
                LoadSubTableData(connection, b);
                return b;
            }

            return (Book?)null;
        });

        if (book is not null)
        {
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
        }

        return book;
    }

    public async Task<Book?> FindByPath(string sourcePath)
    {
        var book = await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM Books WHERE SourcePath = @path COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@path", sourcePath);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var b = MapBook(reader);
                LoadSubTableData(connection, b);
                return b;
            }

            return (Book?)null;
        });

        if (book is not null)
        {
            book.Authors = await authorRepository.GetAuthorsByBookUuid(book.BookUuid);
            book.Series = await seriesRepository.GetSeriesByBookUuid(book.BookUuid);
        }

        return book;
    }

    public async Task Add(Book book)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Books (BookUuid, EntitlementUuid, MediaType, SourcePath, Title, Subtitle, Language, Isbn, PublicationDate, Description, Publisher, CoverFilePath, KepubFilePath, TotalFileSize, PageCount, WordCount, Duration, IsAbridged, CreatedAt, ModifiedAt, IsArchived, Rights, Source, Type, Relation, Coverage, Format)
                VALUES (@bookUuid, @entitlementUuid, @mediaType, @sourcePath, @title, @subtitle, @language, @isbn, @publicationDate, @description, @publisher, @coverFilePath, @kepubFilePath, @totalFileSize, @pageCount, @wordCount, @duration, @isAbridged, @createdAt, @modifiedAt, @isArchived, @rights, @source, @type, @relation, @coverage, @format)
                """;
            AddBookParameters(cmd, book);
            cmd.ExecuteNonQuery();
            SaveSubTableData(connection, book);
        });
    }

    public async Task Update(Book book)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE Books SET EntitlementUuid = @entitlementUuid, MediaType = @mediaType, SourcePath = @sourcePath, Title = @title, Subtitle = @subtitle, Language = @language, Isbn = @isbn, PublicationDate = @publicationDate, Description = @description, Publisher = @publisher, CoverFilePath = @coverFilePath, KepubFilePath = @kepubFilePath, TotalFileSize = @totalFileSize, PageCount = @pageCount, WordCount = @wordCount, Duration = @duration, IsAbridged = @isAbridged, CreatedAt = @createdAt, ModifiedAt = @modifiedAt, IsArchived = @isArchived, Rights = @rights, Source = @source, Type = @type, Relation = @relation, Coverage = @coverage, Format = @format
                WHERE BookUuid = @bookUuid
                """;
            AddBookParameters(cmd, book);
            cmd.ExecuteNonQuery();
            DeleteSubTableData(connection, book.BookUuid);
            SaveSubTableData(connection, book);
        });
    }

    public async Task Remove(Guid uuid)
    {
        await connectionProvider.Write(connection =>
        {
            DeleteSubTableData(connection, uuid);

            using var deleteBookSeries = connection.CreateCommand();
            deleteBookSeries.CommandText = "DELETE FROM BookSeries WHERE BookUuid = @uuid";
            deleteBookSeries.Parameters.AddWithValue("@uuid", uuid.ToString());
            deleteBookSeries.ExecuteNonQuery();

            using var deleteBookAuthors = connection.CreateCommand();
            deleteBookAuthors.CommandText = "DELETE FROM BookAuthors WHERE BookUuid = @uuid";
            deleteBookAuthors.Parameters.AddWithValue("@uuid", uuid.ToString());
            deleteBookAuthors.ExecuteNonQuery();

            using var deleteCollectionBooks = connection.CreateCommand();
            deleteCollectionBooks.CommandText = "DELETE FROM CollectionBooks WHERE BookUuid = @uuid";
            deleteCollectionBooks.Parameters.AddWithValue("@uuid", uuid.ToString());
            deleteCollectionBooks.ExecuteNonQuery();

            using var deleteReadingStates = connection.CreateCommand();
            deleteReadingStates.CommandText = "DELETE FROM KoboReadingStates WHERE BookUuid = @uuid";
            deleteReadingStates.Parameters.AddWithValue("@uuid", uuid.ToString());
            deleteReadingStates.ExecuteNonQuery();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Books WHERE BookUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", uuid.ToString());
            cmd.ExecuteNonQuery();
        });
    }

    public async Task<List<string>> GetPublishers()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT Publisher FROM Books WHERE Publisher IS NOT NULL AND Publisher != '' ORDER BY Publisher";

            using var reader = cmd.ExecuteReader();
            var publishers = new List<string>();
            while (reader.Read())
                publishers.Add(reader.GetString(0));
            return publishers;
        });
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

    private static void LoadSubTableData(SqliteConnection connection, Book book)
    {
        book.Identifiers = LoadStringSubTable(connection, "BookIdentifiers", book.BookUuid);
        book.Subjects = LoadStringSubTable(connection, "BookSubjects", book.BookUuid);
        book.Contributors = LoadContributors(connection, book.BookUuid);
        if (book.MediaType == MediaType.Audiobook)
            book.Chapters = LoadChapters(connection, book.BookUuid);
    }

    private static List<string> LoadStringSubTable(SqliteConnection connection, string tableName, Guid bookUuid)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT Value FROM {tableName} WHERE BookUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());

        using var reader = cmd.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }

    private static List<Contributor> LoadContributors(SqliteConnection connection, Guid bookUuid)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Name, Role FROM BookContributors WHERE BookUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());

        using var reader = cmd.ExecuteReader();
        var contributors = new List<Contributor>();
        while (reader.Read())
        {
            contributors.Add(new Contributor
            {
                Name = reader.GetString(0),
                Role = reader.IsDBNull(1) ? null : reader.GetString(1)
            });
        }
        return contributors;
    }

    private static List<AudiobookChapter> LoadChapters(SqliteConnection connection, Guid bookUuid)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ChapterIndex, Title, Duration, StartTime, EndTime FROM AudiobookChapters WHERE BookUuid = @uuid ORDER BY ChapterIndex";
        cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());

        using var reader = cmd.ExecuteReader();
        var chapters = new List<AudiobookChapter>();
        while (reader.Read())
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

    private static void SaveSubTableData(SqliteConnection connection, Book book)
    {
        SaveStringSubTable(connection, "BookIdentifiers", book.BookUuid, book.Identifiers);
        SaveStringSubTable(connection, "BookSubjects", book.BookUuid, book.Subjects);
        SaveContributors(connection, book.BookUuid, book.Contributors);
        if (book.Chapters.Count > 0)
            SaveChapters(connection, book.BookUuid, book.Chapters);
    }

    private static void SaveStringSubTable(SqliteConnection connection, string tableName, Guid bookUuid, List<string> values)
    {
        foreach (var value in values)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"INSERT OR IGNORE INTO {tableName} (BookUuid, Value) VALUES (@uuid, @value)";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SaveContributors(SqliteConnection connection, Guid bookUuid, List<Contributor> contributors)
    {
        foreach (var contributor in contributors)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO BookContributors (BookUuid, Name, Role) VALUES (@uuid, @name, @role)";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@name", contributor.Name);
            cmd.Parameters.AddWithValue("@role", (object?)contributor.Role ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SaveChapters(SqliteConnection connection, Guid bookUuid, List<AudiobookChapter> chapters)
    {
        foreach (var chapter in chapters)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO AudiobookChapters (BookUuid, ChapterIndex, Title, Duration, StartTime, EndTime) VALUES (@uuid, @index, @title, @duration, @startTime, @endTime)";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@index", chapter.ChapterIndex);
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@duration", chapter.Duration);
            cmd.Parameters.AddWithValue("@startTime", chapter.StartTime);
            cmd.Parameters.AddWithValue("@endTime", chapter.EndTime);
            cmd.ExecuteNonQuery();
        }
    }

    private static void DeleteSubTableData(SqliteConnection connection, Guid bookUuid)
    {
        foreach (var table in new[] { "BookIdentifiers", "BookSubjects", "BookContributors", "AudiobookChapters" })
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table} WHERE BookUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            cmd.ExecuteNonQuery();
        }
    }
}
