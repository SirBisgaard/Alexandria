using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class DatabaseInitializer(AlexandriaSettings settings) : IDatabaseInitializer
{
    public async Task Initialize()
    {
        var directory = Path.GetDirectoryName(settings.Database);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection($"Data Source={settings.Database}");
        await connection.OpenAsync();

        await EnsureSchemaVersionTable(connection);
        var currentVersion = await GetCurrentVersion(connection);

        var migrations = new (int Version, Func<SqliteConnection, Task> Apply)[]
        {
            (1, MigrateV1_InitialSchema)
        };

        foreach (var migration in migrations.Where(m => m.Version > currentVersion).OrderBy(m => m.Version))
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await migration.Apply(connection);
            await SetVersion(connection, migration.Version);
            await transaction.CommitAsync();
        }
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
}
