using Alexandria.Business.KoboStoreProxy.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class KoboDeviceSyncRepository(SqliteConnectionProvider connectionProvider) : IKoboDeviceSyncRepository
{
    public async Task<Dictionary<Guid, DateTimeOffset>> GetSyncedModifiedDates(string deviceId)
    {
        var connection = connectionProvider.Connection;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT BookUuid, BookModifiedAt FROM KoboDeviceSyncedFiles WHERE DeviceId = @deviceId";
        cmd.Parameters.AddWithValue("@deviceId", deviceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new Dictionary<Guid, DateTimeOffset>();
        while (await reader.ReadAsync())
        {
            result[Guid.Parse(reader.GetString(0))] = DateTimeOffset.Parse(reader.GetString(1));
        }

        return result;
    }

    public async Task UpsertSyncedFile(string deviceId, Guid bookUuid, DateTimeOffset bookModifiedAt)
    {
        var connection = connectionProvider.Connection;

        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO KoboDeviceSyncedFiles (DeviceId, BookUuid, BookModifiedAt, CreatedAt, ModifiedAt)
            VALUES (@deviceId, @bookUuid, @bookModifiedAt, @createdAt, @modifiedAt)
            ON CONFLICT (DeviceId, BookUuid) DO UPDATE SET BookModifiedAt = @bookModifiedAt, ModifiedAt = @modifiedAt
            """;
        cmd.Parameters.AddWithValue("@deviceId", deviceId);
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        cmd.Parameters.AddWithValue("@bookModifiedAt", bookModifiedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@createdAt", now);
        cmd.Parameters.AddWithValue("@modifiedAt", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveSyncedFile(string deviceId, Guid bookUuid)
    {
        var connection = connectionProvider.Connection;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM KoboDeviceSyncedFiles WHERE DeviceId = @deviceId AND BookUuid = @bookUuid";
        cmd.Parameters.AddWithValue("@deviceId", deviceId);
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }
}
