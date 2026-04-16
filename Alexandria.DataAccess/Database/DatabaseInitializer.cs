using Alexandria.Business.Authentication.Interfaces;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Database.Interfaces;
using Alexandria.Business.Library.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Database;

public class DatabaseInitializer(ISqliteConnectionProvider connectionProvider, IPasswordHasher passwordHasher, AlexandriaSettings alexandriaSettings, ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task Initialize()
    {
        await connectionProvider.Write(connection =>
        {
            logger.LogInformation("Initializing database");

            EnsureSchemaVersionTable(connection);
            var currentVersion = GetCurrentVersion(connection);
            logger.LogInformation("Current schema version: {Version}", currentVersion);

            var migrations = new (int Version, Action<SqliteConnection> Apply)[]
            {
                (1, conn => MigrateV1_UnifiedSchema(conn, passwordHasher, alexandriaSettings, logger))
            };

            foreach (var migration in migrations.Where(m => m.Version > currentVersion).OrderBy(m => m.Version))
            {
                logger.LogInformation("Applying migration V{Version}", migration.Version);
                using var transaction = connection.BeginTransaction();
                migration.Apply(connection);
                SetVersion(connection, migration.Version);
                transaction.Commit();
            }
        });
    }

    private static void EnsureSchemaVersionTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }

    private static int GetCurrentVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion";
        var result = cmd.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    private static void SetVersion(SqliteConnection connection, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (@version, @appliedAt)";
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@appliedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void MigrateV1_UnifiedSchema(SqliteConnection connection, IPasswordHasher passwordHasher, AlexandriaSettings alexandriaSettings, ILogger logger)
    {
        using var seriesCmd = connection.CreateCommand();
        seriesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Series (
                SeriesUuid TEXT PRIMARY KEY,
                Name TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL
            )
            """;
        seriesCmd.ExecuteNonQuery();

        using var authorsCmd = connection.CreateCommand();
        authorsCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Authors (
                AuthorUuid TEXT PRIMARY KEY,
                Name TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL
            )
            """;
        authorsCmd.ExecuteNonQuery();

        using var booksCmd = connection.CreateCommand();
        booksCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Books (
                BookUuid TEXT PRIMARY KEY,
                EntitlementUuid TEXT NOT NULL,
                Title TEXT NOT NULL,
                Subtitle TEXT,
                Language TEXT NOT NULL,
                Isbn TEXT,
                PublicationDate TEXT,
                Description TEXT,
                Publisher TEXT,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                Rights TEXT,
                Source TEXT,
                Type TEXT,
                Relation TEXT,
                Coverage TEXT,
                Format TEXT
            )
            """;
        booksCmd.ExecuteNonQuery();

        using var bookFilesCmd = connection.CreateCommand();
        bookFilesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BookFiles (
                BookUuid TEXT PRIMARY KEY,
                MediaType TEXT NOT NULL DEFAULT 'Ebook',
                SourcePath TEXT NOT NULL,
                CoverFilePath TEXT,
                KepubFilePath TEXT,
                TotalFileSize INTEGER NOT NULL DEFAULT 0,
                PageCount INTEGER,
                WordCount INTEGER,
                Duration INTEGER,
                IsAbridged INTEGER,
                AudioCodec TEXT,
                AudioBitrate INTEGER,
                FOREIGN KEY (BookUuid) REFERENCES Books(BookUuid)
            )
            """;
        bookFilesCmd.ExecuteNonQuery();

        using var bookIdentifiersCmd = connection.CreateCommand();
        bookIdentifiersCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BookIdentifiers (
                BookUuid TEXT NOT NULL,
                Value TEXT NOT NULL,
                PRIMARY KEY (BookUuid, Value),
                FOREIGN KEY (BookUuid) REFERENCES Books(BookUuid)
            )
            """;
        bookIdentifiersCmd.ExecuteNonQuery();

        using var bookSubjectsCmd = connection.CreateCommand();
        bookSubjectsCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BookSubjects (
                BookUuid TEXT NOT NULL,
                Value TEXT NOT NULL,
                PRIMARY KEY (BookUuid, Value),
                FOREIGN KEY (BookUuid) REFERENCES Books(BookUuid)
            )
            """;
        bookSubjectsCmd.ExecuteNonQuery();

        using var bookContributorsCmd = connection.CreateCommand();
        bookContributorsCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BookContributors (
                BookUuid TEXT NOT NULL,
                Name TEXT NOT NULL,
                Role TEXT,
                PRIMARY KEY (BookUuid, Name),
                FOREIGN KEY (BookUuid) REFERENCES Books(BookUuid)
            )
            """;
        bookContributorsCmd.ExecuteNonQuery();

        using var audiobookChaptersCmd = connection.CreateCommand();
        audiobookChaptersCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AudiobookChapters (
                BookUuid TEXT NOT NULL,
                ChapterIndex INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Duration REAL NOT NULL,
                StartTime REAL NOT NULL,
                EndTime REAL NOT NULL,
                FileName TEXT,
                PRIMARY KEY (BookUuid, ChapterIndex),
                FOREIGN KEY (BookUuid) REFERENCES Books(BookUuid)
            )
            """;
        audiobookChaptersCmd.ExecuteNonQuery();

        using var bookSeriesCmd = connection.CreateCommand();
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
        bookSeriesCmd.ExecuteNonQuery();

        using var bookAuthorsCmd = connection.CreateCommand();
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
        bookAuthorsCmd.ExecuteNonQuery();

        using var readingStatesCmd = connection.CreateCommand();
        readingStatesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS KoboReadingStates (
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
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL
            )
            """;
        readingStatesCmd.ExecuteNonQuery();

        using var collectionsCmd = connection.CreateCommand();
        collectionsCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Collections (
                CollectionUuid TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL
            )
            """;
        collectionsCmd.ExecuteNonQuery();

        using var collectionBooksCmd = connection.CreateCommand();
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
        collectionBooksCmd.ExecuteNonQuery();

        using var syncedFilesCmd = connection.CreateCommand();
        syncedFilesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS KoboDeviceSyncedFiles (
                DeviceUuid TEXT NOT NULL,
                BookUuid TEXT NOT NULL,
                BookModifiedAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                PRIMARY KEY (DeviceUuid, BookUuid)
            )
            """;
        syncedFilesCmd.ExecuteNonQuery();

        using var devicesCmd = connection.CreateCommand();
        devicesCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS KoboDevices (
                DeviceUuid TEXT PRIMARY KEY,
                Nickname TEXT NOT NULL,
                SerialNumber TEXT,
                IsLocked INTEGER NOT NULL DEFAULT 0,
                ModelName TEXT,
                ModelCode TEXT,
                Firmware TEXT,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                LastSyncedAt TEXT NOT NULL
            )
            """;
        devicesCmd.ExecuteNonQuery();

        using var logsCmd = connection.CreateCommand();
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
        logsCmd.ExecuteNonQuery();

        using var settingsCreateCmd = connection.CreateCommand();
        settingsCreateCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Settings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                UuidSeed TEXT NOT NULL,
                LastIndexedTimestamp TEXT,
                SyncBatchSize INTEGER NOT NULL DEFAULT 60,
                ProxyLoggingEnabled INTEGER NOT NULL DEFAULT 1,
                WordsPerPage INTEGER NOT NULL DEFAULT 250,
                SyncLibraryAtBoot INTEGER NOT NULL DEFAULT 0,
                M4bFileTreatment TEXT NOT NULL DEFAULT 'Nothing'
            )
            """;
        settingsCreateCmd.ExecuteNonQuery();

        // Generate a unique per-install UUID seed. Doubles as UUID v5 namespace, JWT HMAC key seed, and password hashing salt.
        var uuidSeed = Guid.NewGuid();
        var syncBatchSize = 60;
        var proxyLoggingEnabled = true;
        var wordsPerPage = 250;
        var syncLibraryAtBoot = false;
        var m4BFileTreatment = (alexandriaSettings.M4BFileTreatment ?? M4BFileTreatment.Nothing).ToString();

        using var settingsSeedCmd = connection.CreateCommand();
        settingsSeedCmd.CommandText = """
            INSERT INTO Settings (Id, UuidSeed, LastIndexedTimestamp, SyncBatchSize, ProxyLoggingEnabled, WordsPerPage, SyncLibraryAtBoot, M4bFileTreatment)
            VALUES (1, @uuidSeed, NULL, @syncBatchSize, @proxyLoggingEnabled, @wordsPerPage, @syncLibraryAtBoot, @m4bFileTreatment)
            """;
        settingsSeedCmd.Parameters.AddWithValue("@uuidSeed", uuidSeed.ToString());
        settingsSeedCmd.Parameters.AddWithValue("@syncBatchSize", syncBatchSize);
        settingsSeedCmd.Parameters.AddWithValue("@proxyLoggingEnabled", proxyLoggingEnabled ? 1 : 0);
        settingsSeedCmd.Parameters.AddWithValue("@wordsPerPage", wordsPerPage);
        settingsSeedCmd.Parameters.AddWithValue("@syncLibraryAtBoot", syncLibraryAtBoot ? 1 : 0);
        settingsSeedCmd.Parameters.AddWithValue("@m4bFileTreatment", m4BFileTreatment);
        settingsSeedCmd.ExecuteNonQuery();
        logger.LogInformation("Database seeded with settings — UuidSeed: {UuidSeed}, SyncBatchSize: {SyncBatchSize}, ProxyLoggingEnabled: {ProxyLoggingEnabled}, WordsPerPage: {WordsPerPage}, SyncLibraryAtBoot: {SyncLibraryAtBoot}, M4bFileTreatment: {M4bFileTreatment}", uuidSeed, syncBatchSize, proxyLoggingEnabled, wordsPerPage, syncLibraryAtBoot, m4BFileTreatment);

        using var usersCmd = connection.CreateCommand();
        usersCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                UserUuid TEXT PRIMARY KEY,
                Username TEXT NOT NULL UNIQUE,
                Password TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NOT NULL,
                LastLoginAt TEXT NOT NULL
            )
            """;
        usersCmd.ExecuteNonQuery();

        // Generate the default user.
        GuidExtensions.SetNamespace(uuidSeed);
        var defaultUserUuid = Guid.GenerateUuidV5("Alexander");
        var defaultPasswordHash = passwordHasher.Hash("TheGreat");
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var userSeedCmd = connection.CreateCommand();
        userSeedCmd.CommandText = """
            INSERT INTO Users (UserUuid, Username, Password, DisplayName, CreatedAt, ModifiedAt, LastLoginAt)
            VALUES (@userUuid, @username, @password, @displayName, @createdAt, @modifiedAt, @lastLoginAt)
            """;
        userSeedCmd.Parameters.AddWithValue("@userUuid", defaultUserUuid.ToString());
        userSeedCmd.Parameters.AddWithValue("@username", "alexander");
        userSeedCmd.Parameters.AddWithValue("@password", defaultPasswordHash);
        userSeedCmd.Parameters.AddWithValue("@displayName", "Alexander The Great");
        userSeedCmd.Parameters.AddWithValue("@createdAt", now);
        userSeedCmd.Parameters.AddWithValue("@modifiedAt", now);
        userSeedCmd.Parameters.AddWithValue("@lastLoginAt", now);
        userSeedCmd.ExecuteNonQuery();
    }
}
