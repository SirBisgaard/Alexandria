using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess;

public class DatabaseInitializer(AlexandriaSettings settings, ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task Initialize()
    {
        logger.LogInformation("Database initialization started. Database: {Database}", settings.Database);

        var directory = Path.GetDirectoryName(settings.Database);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection($"Data Source={settings.Database}");
        await connection.OpenAsync();

        await EnsureSchemaVersionTable(connection);
        var currentVersion = await GetCurrentVersion(connection);
        logger.LogInformation("Current schema version: {Version}", currentVersion);

        var migrations = new (int Version, Func<SqliteConnection, Task> Apply)[]
        {
            (1, MigrateV1_UnifiedSchema)
        };

        foreach (var migration in migrations.Where(m => m.Version > currentVersion).OrderBy(m => m.Version))
        {
            logger.LogInformation("Applying migration V{Version}", migration.Version);
            await using var transaction = await connection.BeginTransactionAsync();
            await migration.Apply(connection);
            await SetVersion(connection, migration.Version);
            await transaction.CommitAsync();
            logger.LogInformation("Migration V{Version} applied successfully", migration.Version);
        }

        logger.LogInformation("Database initialization complete");
    }

    private static async Task EnsureSchemaVersionTable(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL)";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> GetCurrentVersion(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task SetVersion(SqliteConnection connection, int version)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (@version, @appliedAt)";
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@appliedAt", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task MigrateV1_UnifiedSchema(SqliteConnection connection)
    {
        await using var seriesCmd = connection.CreateCommand();
        seriesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Series (
                SeriesUuid TEXT PRIMARY KEY,
                Name TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL
            )
            """;
        await seriesCmd.ExecuteNonQueryAsync();

        await using var authorsCmd = connection.CreateCommand();
        authorsCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Authors (
                AuthorUuid TEXT PRIMARY KEY,
                Name TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL
            )
            """;
        await authorsCmd.ExecuteNonQueryAsync();

        await using var booksCmd = connection.CreateCommand();
        booksCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Books (
                BookUuid TEXT PRIMARY KEY,
                EntitlementUuid TEXT NOT NULL,
                FileHash TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                Title TEXT NOT NULL,
                Language TEXT NOT NULL,
                PublicationDate TEXT,
                Description TEXT,
                Publisher TEXT,
                CoverFilePath TEXT,
                KepubFilePath TEXT,
                KepubFileSize INTEGER NOT NULL DEFAULT 0,
                CoverImageUuid TEXT,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                IsArchived INTEGER NOT NULL DEFAULT 0
            )
            """;
        await booksCmd.ExecuteNonQueryAsync();

        await using var bookSeriesCmd = connection.CreateCommand();
        bookSeriesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BookSeries (
                BookUuid TEXT NOT NULL,
                SeriesUuid TEXT NOT NULL,
                [Index] REAL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                PRIMARY KEY (BookUuid, SeriesUuid),
                FOREIGN KEY (BookUuid) REFERENCES Books(BookUuid),
                FOREIGN KEY (SeriesUuid) REFERENCES Series(SeriesUuid)
            )
            """;
        await bookSeriesCmd.ExecuteNonQueryAsync();

        await using var bookAuthorsCmd = connection.CreateCommand();
        bookAuthorsCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BookAuthors (
                BookUuid TEXT NOT NULL,
                AuthorUuid TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                PRIMARY KEY (BookUuid, AuthorUuid),
                FOREIGN KEY (BookUuid) REFERENCES Books(BookUuid),
                FOREIGN KEY (AuthorUuid) REFERENCES Authors(AuthorUuid)
            )
            """;
        await bookAuthorsCmd.ExecuteNonQueryAsync();

        await using var readingStatesCmd = connection.CreateCommand();
        readingStatesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ReadingStates (
                BookUuid TEXT PRIMARY KEY,
                StatusInfoStatus TEXT NOT NULL DEFAULT 'ReadyToRead',
                StatusInfoLastModified TEXT NOT NULL,
                TimesStartedReading INTEGER NOT NULL DEFAULT 0,
                LastTimeStartedReading TEXT,
                SpentReadingMinutes INTEGER,
                RemainingTimeMinutes INTEGER,
                StatisticsLastModified TEXT NOT NULL,
                ProgressPercent REAL,
                ContentSourceProgressPercent REAL,
                LocationValue TEXT,
                LocationType TEXT,
                LocationSource TEXT,
                BookmarkLastModified TEXT NOT NULL,
                LastModified TEXT NOT NULL,
                PriorityTimestamp TEXT NOT NULL,
                Created TEXT NOT NULL,
                RawPutBody TEXT,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL
            )
            """;
        await readingStatesCmd.ExecuteNonQueryAsync();

        await using var collectionsCmd = connection.CreateCommand();
        collectionsCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Collections (
                CollectionUuid TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL
            )
            """;
        await collectionsCmd.ExecuteNonQueryAsync();

        await using var collectionBooksCmd = connection.CreateCommand();
        collectionBooksCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CollectionBooks (
                CollectionUuid TEXT NOT NULL,
                BookUuid TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                PRIMARY KEY (CollectionUuid, BookUuid),
                FOREIGN KEY (CollectionUuid) REFERENCES Collections(CollectionUuid),
                FOREIGN KEY (BookUuid) REFERENCES Books(BookUuid)
            )
            """;
        await collectionBooksCmd.ExecuteNonQueryAsync();

        await using var syncedFilesCmd = connection.CreateCommand();
        syncedFilesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS KoboDeviceSyncedFiles (
                DeviceId TEXT NOT NULL,
                BookUuid TEXT NOT NULL,
                FileHash TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                PRIMARY KEY (DeviceId, BookUuid)
            )
            """;
        await syncedFilesCmd.ExecuteNonQueryAsync();

        await using var logsCmd = connection.CreateCommand();
        logsCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS KoboTrafficLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Method TEXT NOT NULL,
                Url TEXT NOT NULL,
                RequestHeaders TEXT,
                RequestBody TEXT,
                StatusCode INTEGER NOT NULL,
                ResponseHeaders TEXT,
                ResponseBody TEXT,
                DurationMs INTEGER NOT NULL
            )
            """;
        await logsCmd.ExecuteNonQueryAsync();

        await using var settingsCreateCmd = connection.CreateCommand();
        settingsCreateCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Settings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                UuidSeed TEXT NOT NULL,
                LastIndexedTimestamp TEXT,
                SyncBatchSize INTEGER NOT NULL DEFAULT 60,
                ProxyLoggingEnabled INTEGER NOT NULL DEFAULT 1
            )
            """;
        await settingsCreateCmd.ExecuteNonQueryAsync();

        await using var settingsSeedCmd = connection.CreateCommand();
        settingsSeedCmd.CommandText = """
            INSERT INTO Settings (Id, UuidSeed, LastIndexedTimestamp, SyncBatchSize, ProxyLoggingEnabled)
            VALUES (1, '00000000-0000-0000-0000-000000000004', NULL, 60, 1)
            """;
        await settingsSeedCmd.ExecuteNonQueryAsync();
    }
}
