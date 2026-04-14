using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.DataAccess.Database.Repositories;

public class KoboDeviceSyncRepository(ISqliteConnectionProvider connectionProvider) : IKoboDeviceSyncRepository
{
    public async Task<Dictionary<Guid, DateTimeOffset>> GetSyncedModifiedDates(string deviceUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT BookUuid, BookModifiedAt FROM KoboDeviceSyncedFiles WHERE DeviceUuid = @deviceUuid";
            cmd.Parameters.AddWithValue("@deviceUuid", deviceUuid);

            using var reader = cmd.ExecuteReader();
            var result = new Dictionary<Guid, DateTimeOffset>();
            while (reader.Read())
            {
                result[Guid.Parse(reader.GetString(0))] = DateTimeOffset.Parse(reader.GetString(1));
            }

            return result;
        });
    }

    public async Task UpsertSyncedFile(string deviceUuid, Guid bookUuid, DateTimeOffset bookModifiedAt)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO KoboDeviceSyncedFiles (DeviceUuid, BookUuid, BookModifiedAt, CreatedAt, ModifiedAt)
                VALUES (@deviceUuid, @bookUuid, @bookModifiedAt, @createdAt, @modifiedAt)
                ON CONFLICT (DeviceUuid, BookUuid) DO UPDATE SET BookModifiedAt = @bookModifiedAt, ModifiedAt = @modifiedAt
                """;
            cmd.Parameters.AddWithValue("@deviceUuid", deviceUuid);
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@bookModifiedAt", bookModifiedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@createdAt", now);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task RemoveSyncedFile(string deviceUuid, Guid bookUuid)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM KoboDeviceSyncedFiles WHERE DeviceUuid = @deviceUuid AND BookUuid = @bookUuid";
            cmd.Parameters.AddWithValue("@deviceUuid", deviceUuid);
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            cmd.ExecuteNonQuery();
        });
    }
}
