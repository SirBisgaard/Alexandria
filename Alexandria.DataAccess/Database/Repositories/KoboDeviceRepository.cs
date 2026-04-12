using System.Text.RegularExpressions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.DataAccess.Database.Repositories;

public partial class KoboDeviceRepository(ISqliteConnectionProvider connectionProvider) : IKoboDeviceRepository
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

    public async Task Upsert(KoboDevice device)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO KoboDevices (DeviceUuid, DeviceId, ModelName, ModelCode, Firmware, CreatedAt, ModifiedAt, LastRequestAt)
                VALUES (@deviceUuid, @deviceId, @modelName, @modelCode, @firmware, @createdAt, @modifiedAt, @lastRequestAt)
                ON CONFLICT (DeviceId) DO UPDATE SET
                    ModelName = COALESCE(@modelName, ModelName),
                    ModelCode = COALESCE(@modelCode, ModelCode),
                    Firmware = COALESCE(@firmware, Firmware),
                    ModifiedAt = @modifiedAt,
                    LastRequestAt = @lastRequestAt
                """;
            cmd.Parameters.AddWithValue("@deviceUuid", device.DeviceUuid.ToString());
            cmd.Parameters.AddWithValue("@deviceId", device.DeviceId);
            cmd.Parameters.AddWithValue("@modelName", (object?)device.ModelName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@modelCode", (object?)device.ModelCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@firmware", (object?)device.Firmware ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@createdAt", now);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.Parameters.AddWithValue("@lastRequestAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task<KoboDevice?> GetByDeviceId(string deviceId)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT DeviceUuid, DeviceId, ModelName, ModelCode, Firmware, CreatedAt, ModifiedAt, LastRequestAt FROM KoboDevices WHERE DeviceId = @deviceId";
            cmd.Parameters.AddWithValue("@deviceId", deviceId);

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
            cmd.CommandText = "SELECT DeviceUuid, DeviceId, ModelName, ModelCode, Firmware, CreatedAt, ModifiedAt, LastRequestAt FROM KoboDevices";

            using var reader = cmd.ExecuteReader();
            var result = new List<KoboDevice>();
            while (reader.Read())
                result.Add(MapDevice(reader));

            return result;
        });
    }

    private static KoboDevice MapDevice(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new KoboDevice
        {
            DeviceUuid = Guid.Parse(reader.GetString(0)),
            DeviceId = reader.GetString(1),
            ModelName = reader.IsDBNull(2) ? null : reader.GetString(2),
            ModelCode = reader.IsDBNull(3) ? null : reader.GetString(3),
            Firmware = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(5)),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(6)),
            LastRequestAt = DateTimeOffset.Parse(reader.GetString(7))
        };
    }

    [GeneratedRegex(@"\(Kobo Touch (\d{4})/([\d.]+)\)")]
    private static partial Regex KoboUserAgentRegex();
}
