using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class BookRepository(ISqliteConnectionProvider connectionProvider, IAuthorRepository authorRepository, ISeriesRepository seriesRepository) : IBookRepository
{
    private const string SelectBooksWithFiles = """
        SELECT b.BookUuid, b.EntitlementUuid, b.Title, b.Subtitle, b.Language, b.Isbn, b.PublicationDate, b.Description, b.Publisher, b.CreatedAt, b.ModifiedAt, b.IsArchived, b.Rights, b.Source, b.Type, b.Relation, b.Coverage, b.Format,
               f.MediaType, f.SourcePath, f.CoverFilePath, f.KepubFilePath, f.TotalFileSize, f.PageCount, f.WordCount, f.Duration, f.IsAbridged, f.AudioCodec, f.AudioBitrate, f.ReadingDirection, f.ColorMode
        FROM Books b
        INNER JOIN BookFiles f ON b.BookUuid = f.BookUuid
        """;

    public async Task<List<Book>> GetAll()
    {
        var books = await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SelectBooksWithFiles;

            using var reader = cmd.ExecuteReader();
            var result = new List<Book>();
            while (reader.Read())
            {
                result.Add(MapBook(reader));
            }

            var identifiers = LoadStringSubTableForAll(connection, "BookIdentifiers");
            var subjects = LoadStringSubTableForAll(connection, "BookSubjects");
            var contributors = LoadContributorsForAll(connection);
            var chapters = LoadChaptersForAll(connection);

            foreach (var book in result)
            {
                book.Identifiers = identifiers.GetValueOrDefault(book.BookUuid, []);
                book.Subjects = subjects.GetValueOrDefault(book.BookUuid, []);
                book.Contributors = contributors.GetValueOrDefault(book.BookUuid, []);
                if (book.File.MediaType == MediaType.Audiobook)
                    book.Chapters = chapters.GetValueOrDefault(book.BookUuid, []);
            }

            return result;
        });

        var authorsByBook = await authorRepository.GetAuthorsForAllBooks();
        var seriesByBook = await seriesRepository.GetSeriesForAllBooks();
        foreach (var book in books)
        {
            book.Authors = authorsByBook.GetValueOrDefault(book.BookUuid, []);
            book.Series = seriesByBook.GetValueOrDefault(book.BookUuid);
        }

        return books;
    }

    public async Task<List<BookCoverKey>> GetCoverLookupKeys()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT BookUuid, EntitlementUuid, ModifiedAt FROM Books";
            using var reader = cmd.ExecuteReader();
            var result = new List<BookCoverKey>();
            while (reader.Read())
            {
                result.Add(new BookCoverKey
                {
                    BookUuid = Guid.Parse(reader.GetString(0)),
                    EntitlementUuid = Guid.Parse(reader.GetString(1)),
                    ModifiedAt = DateTimeOffset.Parse(reader.GetString(2))
                });
            }
            return result;
        });
    }

    public async Task<Book?> FindByUuid(Guid uuid)
    {
        var book = await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = SelectBooksWithFiles + " WHERE b.BookUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", uuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var b = MapBook(reader);
                LoadSubTableData(connection, b);
                return b;
            }

            return null;
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
            cmd.CommandText = SelectBooksWithFiles + " WHERE b.EntitlementUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", entitlementUuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var b = MapBook(reader);
                LoadSubTableData(connection, b);
                return b;
            }

            return null;
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
            cmd.CommandText = SelectBooksWithFiles + " WHERE f.SourcePath = @path COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@path", sourcePath);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var b = MapBook(reader);
                LoadSubTableData(connection, b);
                return b;
            }

            return null;
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
            using var bookCmd = connection.CreateCommand();
            bookCmd.CommandText = """
                INSERT INTO Books (BookUuid, EntitlementUuid, Title, Subtitle, Language, Isbn, PublicationDate, Description, Publisher, CreatedAt, ModifiedAt, IsArchived, Rights, Source, Type, Relation, Coverage, Format)
                VALUES (@bookUuid, @entitlementUuid, @title, @subtitle, @language, @isbn, @publicationDate, @description, @publisher, @createdAt, @modifiedAt, @isArchived, @rights, @source, @type, @relation, @coverage, @format)
                """;
            AddBookParameters(bookCmd, book);
            bookCmd.ExecuteNonQuery();

            using var fileCmd = connection.CreateCommand();
            fileCmd.CommandText = """
                INSERT INTO BookFiles (BookUuid, MediaType, SourcePath, CoverFilePath, KepubFilePath, TotalFileSize, PageCount, WordCount, Duration, IsAbridged, AudioCodec, AudioBitrate, ReadingDirection, ColorMode)
                VALUES (@bookUuid, @mediaType, @sourcePath, @coverFilePath, @kepubFilePath, @totalFileSize, @pageCount, @wordCount, @duration, @isAbridged, @audioCodec, @audioBitrate, @readingDirection, @colorMode)
                """;
            AddBookFileParameters(fileCmd, book);
            fileCmd.ExecuteNonQuery();

            SaveSubTableData(connection, book);
        });
    }

    public async Task Update(Book book)
    {
        await connectionProvider.Write(connection =>
        {
            using var bookCmd = connection.CreateCommand();
            bookCmd.CommandText = """
                UPDATE Books SET EntitlementUuid = @entitlementUuid, Title = @title, Subtitle = @subtitle, Language = @language, Isbn = @isbn, PublicationDate = @publicationDate, Description = @description, Publisher = @publisher, CreatedAt = @createdAt, ModifiedAt = @modifiedAt, IsArchived = @isArchived, Rights = @rights, Source = @source, Type = @type, Relation = @relation, Coverage = @coverage, Format = @format
                WHERE BookUuid = @bookUuid
                """;
            AddBookParameters(bookCmd, book);
            bookCmd.ExecuteNonQuery();

            using var fileCmd = connection.CreateCommand();
            fileCmd.CommandText = """
                UPDATE BookFiles SET MediaType = @mediaType, SourcePath = @sourcePath, CoverFilePath = @coverFilePath, KepubFilePath = @kepubFilePath, TotalFileSize = @totalFileSize, PageCount = @pageCount, WordCount = @wordCount, Duration = @duration, IsAbridged = @isAbridged, AudioCodec = @audioCodec, AudioBitrate = @audioBitrate, ReadingDirection = @readingDirection, ColorMode = @colorMode
                WHERE BookUuid = @bookUuid
                """;
            AddBookFileParameters(fileCmd, book);
            fileCmd.ExecuteNonQuery();

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

            using var deleteSyncedFiles = connection.CreateCommand();
            deleteSyncedFiles.CommandText = "DELETE FROM KoboDeviceSyncedFiles WHERE BookUuid = @uuid";
            deleteSyncedFiles.Parameters.AddWithValue("@uuid", uuid.ToString());
            deleteSyncedFiles.ExecuteNonQuery();

            using var deleteBookFiles = connection.CreateCommand();
            deleteBookFiles.CommandText = "DELETE FROM BookFiles WHERE BookUuid = @uuid";
            deleteBookFiles.Parameters.AddWithValue("@uuid", uuid.ToString());
            deleteBookFiles.ExecuteNonQuery();

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
        cmd.Parameters.AddWithValue("@title", book.Title);
        cmd.Parameters.AddWithValue("@subtitle", (object?)book.Subtitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@language", book.Language);
        cmd.Parameters.AddWithValue("@isbn", (object?)book.Isbn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@publicationDate", (object?)book.PublicationDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@description", (object?)book.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@publisher", (object?)book.Publisher ?? DBNull.Value);
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

    private static void AddBookFileParameters(SqliteCommand cmd, Book book)
    {
        cmd.Parameters.AddWithValue("@bookUuid", book.BookUuid.ToString());
        cmd.Parameters.AddWithValue("@mediaType", book.File.MediaType.ToString());
        cmd.Parameters.AddWithValue("@sourcePath", book.File.SourcePath);
        cmd.Parameters.AddWithValue("@coverFilePath", (object?)book.File.CoverFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kepubFilePath", (object?)book.File.KepubFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@totalFileSize", book.File.TotalFileSize);
        cmd.Parameters.AddWithValue("@pageCount", (object?)book.File.PageCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wordCount", (object?)book.File.WordCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@duration", (object?)book.File.Duration ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isAbridged", book.File.IsAbridged.HasValue ? (book.File.IsAbridged.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("@audioCodec", (object?)book.File.AudioCodec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@audioBitrate", (object?)book.File.AudioBitrate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@readingDirection", (object?)book.File.ReadingDirection?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@colorMode", (object?)book.File.ColorMode?.ToString() ?? DBNull.Value);
    }

    private static Book MapBook(SqliteDataReader reader)
    {
        return new Book
        {
            BookUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("BookUuid"))),
            EntitlementUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("EntitlementUuid"))),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Subtitle = reader.IsDBNull(reader.GetOrdinal("Subtitle")) ? null : reader.GetString(reader.GetOrdinal("Subtitle")),
            Language = reader.GetString(reader.GetOrdinal("Language")),
            Isbn = reader.IsDBNull(reader.GetOrdinal("Isbn")) ? null : reader.GetString(reader.GetOrdinal("Isbn")),
            PublicationDate = reader.IsDBNull(reader.GetOrdinal("PublicationDate")) ? null : reader.GetString(reader.GetOrdinal("PublicationDate")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
            Publisher = reader.IsDBNull(reader.GetOrdinal("Publisher")) ? null : reader.GetString(reader.GetOrdinal("Publisher")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ModifiedAt"))),
            IsArchived = reader.GetInt32(reader.GetOrdinal("IsArchived")) == 1,
            Rights = reader.IsDBNull(reader.GetOrdinal("Rights")) ? null : reader.GetString(reader.GetOrdinal("Rights")),
            Source = reader.IsDBNull(reader.GetOrdinal("Source")) ? null : reader.GetString(reader.GetOrdinal("Source")),
            Type = reader.IsDBNull(reader.GetOrdinal("Type")) ? null : reader.GetString(reader.GetOrdinal("Type")),
            Relation = reader.IsDBNull(reader.GetOrdinal("Relation")) ? null : reader.GetString(reader.GetOrdinal("Relation")),
            Coverage = reader.IsDBNull(reader.GetOrdinal("Coverage")) ? null : reader.GetString(reader.GetOrdinal("Coverage")),
            Format = reader.IsDBNull(reader.GetOrdinal("Format")) ? null : reader.GetString(reader.GetOrdinal("Format")),
            File = new BookFile
            {
                BookUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("BookUuid"))),
                MediaType = Enum.Parse<MediaType>(reader.GetString(reader.GetOrdinal("MediaType"))),
                SourcePath = reader.GetString(reader.GetOrdinal("SourcePath")),
                CoverFilePath = reader.IsDBNull(reader.GetOrdinal("CoverFilePath")) ? null : reader.GetString(reader.GetOrdinal("CoverFilePath")),
                KepubFilePath = reader.IsDBNull(reader.GetOrdinal("KepubFilePath")) ? null : reader.GetString(reader.GetOrdinal("KepubFilePath")),
                TotalFileSize = reader.GetInt64(reader.GetOrdinal("TotalFileSize")),
                PageCount = reader.IsDBNull(reader.GetOrdinal("PageCount")) ? null : reader.GetInt32(reader.GetOrdinal("PageCount")),
                WordCount = reader.IsDBNull(reader.GetOrdinal("WordCount")) ? null : reader.GetInt32(reader.GetOrdinal("WordCount")),
                Duration = reader.IsDBNull(reader.GetOrdinal("Duration")) ? null : reader.GetInt32(reader.GetOrdinal("Duration")),
                IsAbridged = reader.IsDBNull(reader.GetOrdinal("IsAbridged")) ? null : reader.GetInt32(reader.GetOrdinal("IsAbridged")) == 1,
                AudioCodec = reader.IsDBNull(reader.GetOrdinal("AudioCodec")) ? null : reader.GetString(reader.GetOrdinal("AudioCodec")),
                AudioBitrate = reader.IsDBNull(reader.GetOrdinal("AudioBitrate")) ? null : reader.GetInt32(reader.GetOrdinal("AudioBitrate")),
                ReadingDirection = reader.IsDBNull(reader.GetOrdinal("ReadingDirection")) ? null : Enum.Parse<ComicReadingDirection>(reader.GetString(reader.GetOrdinal("ReadingDirection"))),
                ColorMode = reader.IsDBNull(reader.GetOrdinal("ColorMode")) ? null : Enum.Parse<ComicColorMode>(reader.GetString(reader.GetOrdinal("ColorMode")))
            }
        };
    }

    private static void LoadSubTableData(SqliteConnection connection, Book book)
    {
        book.Identifiers = LoadStringSubTable(connection, "BookIdentifiers", book.BookUuid);
        book.Subjects = LoadStringSubTable(connection, "BookSubjects", book.BookUuid);
        book.Contributors = LoadContributors(connection, book.BookUuid);
        if (book.File.MediaType == MediaType.Audiobook)
            book.Chapters = LoadChapters(connection, book.BookUuid);
    }

    private static Dictionary<Guid, List<string>> LoadStringSubTableForAll(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT BookUuid, Value FROM {tableName}";

        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<Guid, List<string>>();
        while (reader.Read())
        {
            var bookUuid = Guid.Parse(reader.GetString(0));
            if (!result.TryGetValue(bookUuid, out var values))
                result[bookUuid] = values = [];
            values.Add(reader.GetString(1));
        }
        return result;
    }

    private static Dictionary<Guid, List<Contributor>> LoadContributorsForAll(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT BookUuid, Name, Role FROM BookContributors";

        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<Guid, List<Contributor>>();
        while (reader.Read())
        {
            var bookUuid = Guid.Parse(reader.GetString(0));
            if (!result.TryGetValue(bookUuid, out var contributors))
                result[bookUuid] = contributors = [];
            contributors.Add(new Contributor
            {
                Name = reader.GetString(1),
                Role = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }
        return result;
    }

    private static Dictionary<Guid, List<AudiobookChapter>> LoadChaptersForAll(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT BookUuid, ChapterIndex, Title, Duration, StartTime, EndTime, FileName FROM AudiobookChapters ORDER BY BookUuid, ChapterIndex";

        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<Guid, List<AudiobookChapter>>();
        while (reader.Read())
        {
            var bookUuid = Guid.Parse(reader.GetString(0));
            if (!result.TryGetValue(bookUuid, out var chapters))
                result[bookUuid] = chapters = [];
            chapters.Add(new AudiobookChapter
            {
                ChapterIndex = reader.GetInt32(1),
                Title = reader.GetString(2),
                Duration = reader.GetDouble(3),
                StartTime = reader.GetDouble(4),
                EndTime = reader.GetDouble(5),
                FileName = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return result;
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
        cmd.CommandText = "SELECT ChapterIndex, Title, Duration, StartTime, EndTime, FileName FROM AudiobookChapters WHERE BookUuid = @uuid ORDER BY ChapterIndex";
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
                EndTime = reader.GetDouble(4),
                FileName = reader.IsDBNull(5) ? null : reader.GetString(5)
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
            cmd.CommandText = "INSERT OR REPLACE INTO AudiobookChapters (BookUuid, ChapterIndex, Title, Duration, StartTime, EndTime, FileName) VALUES (@uuid, @index, @title, @duration, @startTime, @endTime, @fileName)";
            cmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@index", chapter.ChapterIndex);
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@duration", chapter.Duration);
            cmd.Parameters.AddWithValue("@startTime", chapter.StartTime);
            cmd.Parameters.AddWithValue("@fileName", (object?)chapter.FileName ?? DBNull.Value);
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
