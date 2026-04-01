using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Repositories;

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
            (1, MigrateV1_InitialSchema),
            (2, MigrateV2_ReadingStates)
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
        cmd.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task MigrateV1_InitialSchema(SqliteConnection connection)
    {
        await using var booksCmd = connection.CreateCommand();
        booksCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Books (
                Uuid TEXT PRIMARY KEY,
                FileHash TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                Title TEXT NOT NULL,
                Authors TEXT NOT NULL,
                Language TEXT NOT NULL,
                PublicationDate TEXT,
                Description TEXT,
                Publisher TEXT,
                SeriesName TEXT,
                SeriesIndex INTEGER,
                CoverFilePath TEXT,
                KepubFilePath TEXT,
                KepubFileSize INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                LastModified TEXT NOT NULL,
                IsArchived INTEGER NOT NULL DEFAULT 0
            )
            """;
        await booksCmd.ExecuteNonQueryAsync();

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
    }

    private static async Task MigrateV2_ReadingStates(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
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
                RawPutBody TEXT
            )
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
