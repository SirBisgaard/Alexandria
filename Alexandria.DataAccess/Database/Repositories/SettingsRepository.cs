using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.DataAccess.Database.Repositories;

public class SettingsRepository(ISqliteConnectionProvider connectionProvider) : ISettingsRepository
{
    public async Task<AppSettings> Get()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT UuidSeed, LastIndexedTimestamp, SyncBatchSize, ProxyLoggingEnabled, WordsPerPage, SyncLibraryAtBoot FROM Settings WHERE Id = 1";

            using var reader = cmd.ExecuteReader();
            reader.Read();

            return new AppSettings
            {
                UuidSeed = Guid.Parse(reader.GetString(reader.GetOrdinal("UuidSeed"))),
                LastIndexedTimestamp = reader.IsDBNull(reader.GetOrdinal("LastIndexedTimestamp")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LastIndexedTimestamp"))),
                SyncBatchSize = reader.GetInt32(reader.GetOrdinal("SyncBatchSize")),
                ProxyLoggingEnabled = reader.GetInt32(reader.GetOrdinal("ProxyLoggingEnabled")) == 1,
                WordsPerPage = reader.GetInt32(reader.GetOrdinal("WordsPerPage")),
                SyncLibraryAtBoot = reader.GetInt32(reader.GetOrdinal("SyncLibraryAtBoot")) == 1
            };
        });
    }

    public async Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Settings SET LastIndexedTimestamp = @timestamp WHERE Id = 1";
            cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("O"));
            cmd.ExecuteNonQuery();
        });
    }

    public async Task UpdateSettings(AppSettings settings)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Settings SET SyncBatchSize = @syncBatchSize, ProxyLoggingEnabled = @proxyLoggingEnabled, WordsPerPage = @wordsPerPage, SyncLibraryAtBoot = @syncLibraryAtBoot WHERE Id = 1";
            cmd.Parameters.AddWithValue("@syncBatchSize", settings.SyncBatchSize);
            cmd.Parameters.AddWithValue("@proxyLoggingEnabled", settings.ProxyLoggingEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@wordsPerPage", settings.WordsPerPage);
            cmd.Parameters.AddWithValue("@syncLibraryAtBoot", settings.SyncLibraryAtBoot ? 1 : 0);
            cmd.ExecuteNonQuery();
        });
    }
}
