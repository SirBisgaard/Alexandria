using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class KoboDeviceSyncRepository(AlexandriaSettings settings) : IKoboDeviceSyncRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task<Dictionary<Guid, string>> GetSyncedFileHashes(string deviceId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT BookUuid, FileHash FROM KoboDeviceSyncedFiles WHERE DeviceId = @deviceId";
        cmd.Parameters.AddWithValue("@deviceId", deviceId);

        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new Dictionary<Guid, string>();
        while (await reader.ReadAsync())
        {
            result[Guid.Parse(reader.GetString(0))] = reader.GetString(1);
        }

        return result;
    }

    public async Task UpsertSyncedFile(string deviceId, Guid bookUuid, string fileHash)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO KoboDeviceSyncedFiles (DeviceId, BookUuid, FileHash, CreatedAt, ModifiedAt)
            VALUES (@deviceId, @bookUuid, @fileHash, @createdAt, @modifiedAt)
            ON CONFLICT (DeviceId, BookUuid) DO UPDATE SET FileHash = @fileHash, ModifiedAt = @modifiedAt
            """;
        cmd.Parameters.AddWithValue("@deviceId", deviceId);
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        cmd.Parameters.AddWithValue("@fileHash", fileHash);
        cmd.Parameters.AddWithValue("@createdAt", now);
        cmd.Parameters.AddWithValue("@modifiedAt", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveSyncedFile(string deviceId, Guid bookUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM KoboDeviceSyncedFiles WHERE DeviceId = @deviceId AND BookUuid = @bookUuid";
        cmd.Parameters.AddWithValue("@deviceId", deviceId);
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }
}
