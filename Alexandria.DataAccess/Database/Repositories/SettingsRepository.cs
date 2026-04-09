using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class SettingsRepository(AlexandriaSettings settings) : ISettingsRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task<AppSettings> Get()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT UuidSeed, LastIndexedTimestamp, SyncBatchSize, ProxyLoggingEnabled, KoboStoreKillSwitch, WordsPerPage FROM Settings WHERE Id = 1";

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new AppSettings
        {
            UuidSeed = Guid.Parse(reader.GetString(reader.GetOrdinal("UuidSeed"))),
            LastIndexedTimestamp = reader.IsDBNull(reader.GetOrdinal("LastIndexedTimestamp")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LastIndexedTimestamp"))),
            SyncBatchSize = reader.GetInt32(reader.GetOrdinal("SyncBatchSize")),
            ProxyLoggingEnabled = reader.GetInt32(reader.GetOrdinal("ProxyLoggingEnabled")) == 1,
            KoboStoreKillSwitch = reader.GetInt32(reader.GetOrdinal("KoboStoreKillSwitch")) == 1,
            WordsPerPage = reader.GetInt32(reader.GetOrdinal("WordsPerPage"))
        };
    }

    public async Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Settings SET LastIndexedTimestamp = @timestamp WHERE Id = 1";
        cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateSettings(AppSettings settings)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Settings SET SyncBatchSize = @syncBatchSize, ProxyLoggingEnabled = @proxyLoggingEnabled, KoboStoreKillSwitch = @koboStoreKillSwitch, WordsPerPage = @wordsPerPage WHERE Id = 1";
        cmd.Parameters.AddWithValue("@syncBatchSize", settings.SyncBatchSize);
        cmd.Parameters.AddWithValue("@proxyLoggingEnabled", settings.ProxyLoggingEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@koboStoreKillSwitch", settings.KoboStoreKillSwitch ? 1 : 0);
        cmd.Parameters.AddWithValue("@wordsPerPage", settings.WordsPerPage);
        await cmd.ExecuteNonQueryAsync();
    }
}
