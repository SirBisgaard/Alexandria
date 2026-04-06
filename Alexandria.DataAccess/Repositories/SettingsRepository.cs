using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class SettingsRepository(AlexandriaSettings settings) : ISettingsRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task<AppSettings> Get()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT UuidSeed, LastIndexedTimestamp, SyncBatchSize FROM Settings WHERE Id = 1";

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new AppSettings
        {
            UuidSeed = Guid.Parse(reader.GetString(reader.GetOrdinal("UuidSeed"))),
            LastIndexedTimestamp = reader.IsDBNull(reader.GetOrdinal("LastIndexedTimestamp")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LastIndexedTimestamp"))),
            SyncBatchSize = reader.GetInt32(reader.GetOrdinal("SyncBatchSize"))
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

    public async Task UpdateSyncBatchSize(int batchSize)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Settings SET SyncBatchSize = @batchSize WHERE Id = 1";
        cmd.Parameters.AddWithValue("@batchSize", batchSize);
        await cmd.ExecuteNonQueryAsync();
    }
}
