using Alexandria.Business.Authentication.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Database;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Data.Database;

/// <summary>
/// Shared setup for database tests. Owns the temp directory, the <see cref="SqliteConnectionProvider"/>,
/// and a freshly-initialized schema. Each subclass test gets its own isolated SQLite database file
/// inside its temp directory (opened in WAL mode by the provider) with the V1 schema already
/// applied and default settings + user rows seeded.
/// </summary>
public abstract class DatabaseTestBase
{
    protected string TempDir { get; private set; } = null!;
    protected AlexandriaSettings Settings { get; private set; } = null!;
    protected SqliteConnectionProvider ConnectionProvider { get; private set; } = null!;

    [TestInitialize]
    public async Task DatabaseTestBase_Setup()
    {
        TempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(TempDir);

        Settings = new AlexandriaSettings
        {
            PublicUrl = "http://localhost",
            DataFolder = TempDir,
            EbookFolder = Path.Combine(TempDir, "ebooks"),
            AudiobookFolder = Path.Combine(TempDir, "audiobooks")
        };

        ConnectionProvider = new SqliteConnectionProvider(Settings);
        var initializer = new DatabaseInitializer(ConnectionProvider, new FakePasswordHasher(), Settings, NullLogger<DatabaseInitializer>.Instance);
        await initializer.Initialize();
    }

    [TestCleanup]
    public void DatabaseTestBase_Cleanup()
    {
        ConnectionProvider?.Dispose();
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }

    /// <summary>
    /// Produces a fully-populated <see cref="Book"/> suitable for direct insertion into a
    /// fresh V1 schema. All fields that the repository persists are set to non-default values
    /// so round-trip assertions can catch column mis-maps. Caller can override anything via the
    /// named parameters; the defaults give a deterministic ebook with one author and one series.
    /// Note: authors and series are set on the returned Book but BookRepository.Add doesn't
    /// persist them — callers must also call AuthorRepository.SetBookAuthors / SeriesRepository.SetBookSeries.
    /// </summary>
    protected static Book BuildBook(
        string title = "Test Book",
        Guid? bookUuid = null,
        Guid? entitlementUuid = null,
        MediaType mediaType = MediaType.Ebook,
        string? publisher = "Alexandria Press",
        string? isbn = "9780000000001",
        string? sourcePath = null,
        bool isArchived = false)
    {
        var uuid = bookUuid ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new Book
        {
            BookUuid = uuid,
            EntitlementUuid = entitlementUuid ?? Guid.NewGuid(),
            Title = title,
            Subtitle = null,
            Language = "en",
            Isbn = isbn,
            PublicationDate = "2024-01-01",
            Description = "Test description.",
            Publisher = publisher,
            Identifiers = isbn is null ? [] : [isbn],
            Subjects = ["Fiction"],
            Rights = null,
            Source = null,
            Type = null,
            Relation = null,
            Coverage = null,
            Format = null,
            CreatedAt = now,
            ModifiedAt = now,
            IsArchived = isArchived,
            File = new BookFile
            {
                BookUuid = uuid,
                MediaType = mediaType,
                SourcePath = sourcePath ?? $"/tmp/books/{uuid}.epub",
                CoverFilePath = null,
                KepubFilePath = null,
                TotalFileSize = 1024,
                PageCount = 42,
                WordCount = 10000,
                Duration = mediaType == MediaType.Audiobook ? 3600 : null,
                IsAbridged = null,
                AudioCodec = mediaType == MediaType.Audiobook ? "aac" : null,
                AudioBitrate = mediaType == MediaType.Audiobook ? 64000 : null
            },
            Authors = [new Author { Name = "Test Author" }],
            Contributors = [],
            Chapters = []
        };
    }

    /// <summary>
    /// Inserts a bare Books row (plus matching BookFiles row) directly via SQL so tests that only
    /// care about child tables (BookAuthors / BookSeries / CollectionBooks / KoboReadingStates /
    /// KoboDeviceSyncedFiles) can satisfy the foreign-key constraints without pulling in the full
    /// <see cref="Alexandria.DataAccess.Database.Repositories.BookRepository"/>.
    /// </summary>
    protected async Task InsertBookShell(Guid bookUuid)
    {
        await ConnectionProvider.Write(conn =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");

            using var bookCmd = conn.CreateCommand();
            bookCmd.CommandText = "INSERT INTO Books (BookUuid, EntitlementUuid, Title, Language, CreatedAt, ModifiedAt) VALUES (@uuid, @euid, 'Shell Title', 'en', @now, @now)";
            bookCmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            bookCmd.Parameters.AddWithValue("@euid", Guid.NewGuid().ToString());
            bookCmd.Parameters.AddWithValue("@now", now);
            bookCmd.ExecuteNonQuery();

            using var fileCmd = conn.CreateCommand();
            fileCmd.CommandText = "INSERT INTO BookFiles (BookUuid, MediaType, SourcePath) VALUES (@uuid, 'Ebook', @path)";
            fileCmd.Parameters.AddWithValue("@uuid", bookUuid.ToString());
            fileCmd.Parameters.AddWithValue("@path", $"/tmp/shell/{bookUuid}.epub");
            fileCmd.ExecuteNonQuery();
        });
    }

    /// <summary>
    /// PBKDF2 with 600k iterations is prohibitively slow for test setup (~200 ms per hash × 100+ tests).
    /// The fake returns a deterministic, round-trippable string sufficient for any test that needs
    /// password storage/verify semantics without exercising the real crypto.
    /// </summary>
    internal sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"fake:{password}";
        public bool Verify(string password, string stored) => stored == $"fake:{password}";
    }
}
