using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.DataAccess.Database.Repositories;

public class KoboDeviceRepository(ISqliteConnectionProvider connectionProvider) : IKoboDeviceRepository
{
    private static readonly Dictionary<string, string> ModelNames = new()
    {
        ["0310"] = "Kobo Mini",
        ["0320"] = "Kobo Glo HD",
        ["0330"] = "Kobo Touch 2.0",
        ["0340"] = "Kobo Aura ONE",
        ["0350"] = "Kobo Aura H2O Edition 2",
        ["0360"] = "Kobo Clara HD",
        ["0370"] = "Kobo Forma",
        ["0371"] = "Kobo Forma",
        ["0373"] = "Kobo Elipsa",
        ["0375"] = "Kobo Sage",
        ["0376"] = "Kobo Libra 2",
        ["0377"] = "Kobo Clara 2E",
        ["0381"] = "Kobo Nia",
        ["0383"] = "Kobo Elipsa 2E",
        ["0384"] = "Kobo Clara BW",
        ["0386"] = "Kobo Clara Colour",
        ["0387"] = "Kobo Libra Colour",
        ["0390"] = "Kobo Libra Colour"
    };

    public async Task Create(KoboDevice device)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO KoboDevices (DeviceUuid, Nickname, CreatedAt, ModifiedAt, LastRequestAt)
                VALUES (@deviceUuid, @nickname, @createdAt, @modifiedAt, @lastRequestAt)
                """;
            cmd.Parameters.AddWithValue("@deviceUuid", device.DeviceUuid.ToString());
            cmd.Parameters.AddWithValue("@nickname", device.Nickname);
            cmd.Parameters.AddWithValue("@createdAt", now);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.Parameters.AddWithValue("@lastRequestAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task<KoboDevice?> GetByUuid(Guid deviceUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT DeviceUuid, Nickname, SerialNumber, IsLocked, ModelName, ModelCode, Firmware, CreatedAt, ModifiedAt, LastRequestAt FROM KoboDevices WHERE DeviceUuid = @deviceUuid";
            cmd.Parameters.AddWithValue("@deviceUuid", deviceUuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return MapDevice(reader);
        });
    }

    public async Task<List<KoboDevice>> GetAll()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT DeviceUuid, Nickname, SerialNumber, IsLocked, ModelName, ModelCode, Firmware, CreatedAt, ModifiedAt, LastRequestAt FROM KoboDevices";

            using var reader = cmd.ExecuteReader();
            var result = new List<KoboDevice>();
            while (reader.Read())
                result.Add(MapDevice(reader));

            return result;
        });
    }

    public async Task Update(KoboDevice device)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE KoboDevices SET Nickname = @nickname, ModifiedAt = @modifiedAt
                WHERE DeviceUuid = @deviceUuid
                """;
            cmd.Parameters.AddWithValue("@deviceUuid", device.DeviceUuid.ToString());
            cmd.Parameters.AddWithValue("@nickname", device.Nickname);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task Delete(Guid deviceUuid)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM KoboDevices WHERE DeviceUuid = @deviceUuid";
            cmd.Parameters.AddWithValue("@deviceUuid", deviceUuid.ToString());
            cmd.ExecuteNonQuery();
        });
    }

    public async Task UpdateLastRequest(Guid deviceUuid)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE KoboDevices SET LastRequestAt = @lastRequestAt, ModifiedAt = @modifiedAt WHERE DeviceUuid = @deviceUuid";
            cmd.Parameters.AddWithValue("@deviceUuid", deviceUuid.ToString());
            cmd.Parameters.AddWithValue("@lastRequestAt", now);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task SetLocked(Guid deviceUuid, bool locked)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE KoboDevices SET IsLocked = @isLocked, ModifiedAt = @modifiedAt WHERE DeviceUuid = @deviceUuid";
            cmd.Parameters.AddWithValue("@deviceUuid", deviceUuid.ToString());
            cmd.Parameters.AddWithValue("@isLocked", locked ? 1 : 0);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task BindDevice(Guid deviceUuid, string serialNumber, string? modelName, string? modelCode, string? firmware)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE KoboDevices SET SerialNumber = @serialNumber, ModelName = COALESCE(@modelName, ModelName), ModelCode = COALESCE(@modelCode, ModelCode), Firmware = COALESCE(@firmware, Firmware), ModifiedAt = @modifiedAt
                WHERE DeviceUuid = @deviceUuid
                """;
            cmd.Parameters.AddWithValue("@deviceUuid", deviceUuid.ToString());
            cmd.Parameters.AddWithValue("@serialNumber", serialNumber);
            cmd.Parameters.AddWithValue("@modelName", (object?)modelName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@modelCode", (object?)modelCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@firmware", (object?)firmware ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public static string? GetModelName(string modelCode)
    {
        return ModelNames.GetValueOrDefault(modelCode);
    }

    private static KoboDevice MapDevice(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new KoboDevice
        {
            DeviceUuid = Guid.Parse(reader.GetString(0)),
            Nickname = reader.GetString(1),
            SerialNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
            IsLocked = reader.GetInt32(3) != 0,
            ModelName = reader.IsDBNull(4) ? null : reader.GetString(4),
            ModelCode = reader.IsDBNull(5) ? null : reader.GetString(5),
            Firmware = reader.IsDBNull(6) ? null : reader.GetString(6),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(7)),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(8)),
            LastRequestAt = DateTimeOffset.Parse(reader.GetString(9))
        };
    }
}
