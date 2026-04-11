using Alexandria.Business.KoboStoreProxy.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class KoboDeviceSyncRepository(ISqliteConnectionProvider connectionProvider) : IKoboDeviceSyncRepository
{
    public Task<Dictionary<Guid, DateTimeOffset>> GetSyncedModifiedDates(string deviceId)
    {
        return Task.FromResult(connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT BookUuid, BookModifiedAt FROM KoboDeviceSyncedFiles WHERE DeviceId = @deviceId";
            cmd.Parameters.AddWithValue("@deviceId", deviceId);

            using var reader = cmd.ExecuteReader();
            var result = new Dictionary<Guid, DateTimeOffset>();
            while (reader.Read())
            {
                result[Guid.Parse(reader.GetString(0))] = DateTimeOffset.Parse(reader.GetString(1));
            }

            return result;
        }));
    }

    public Task UpsertSyncedFile(string deviceId, Guid bookUuid, DateTimeOffset bookModifiedAt)
    {
        connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
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
            cmd.ExecuteNonQuery();
        });
        return Task.CompletedTask;
    }

    public Task RemoveSyncedFile(string deviceId, Guid bookUuid)
    {
        connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM KoboDeviceSyncedFiles WHERE DeviceId = @deviceId AND BookUuid = @bookUuid";
            cmd.Parameters.AddWithValue("@deviceId", deviceId);
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            cmd.ExecuteNonQuery();
        });
        return Task.CompletedTask;
    }
}
