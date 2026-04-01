using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Proxy;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class KoboTrafficLogRepository(AlexandriaSettings settings) : IKoboTrafficLogRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task Log(KoboProxyTrafficLog entry)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO KoboTrafficLogs
                (Timestamp, Method, Url, RequestHeaders, RequestBody,
                 StatusCode, ResponseHeaders, ResponseBody, DurationMs)
            VALUES
                (@ts, @method, @url, @reqHeaders, @reqBody,
                 @status, @resHeaders, @resBody, @duration)
            """;

        cmd.Parameters.AddWithValue("@ts", entry.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@method", entry.Method);
        cmd.Parameters.AddWithValue("@url", entry.Url);
        cmd.Parameters.AddWithValue("@reqHeaders", entry.RequestHeaders);
        cmd.Parameters.AddWithValue("@reqBody", entry.RequestBody);
        cmd.Parameters.AddWithValue("@status", entry.StatusCode);
        cmd.Parameters.AddWithValue("@resHeaders", entry.ResponseHeaders);
        cmd.Parameters.AddWithValue("@resBody", entry.ResponseBody);
        cmd.Parameters.AddWithValue("@duration", entry.DurationMs);

        await cmd.ExecuteNonQueryAsync();
    }
}
