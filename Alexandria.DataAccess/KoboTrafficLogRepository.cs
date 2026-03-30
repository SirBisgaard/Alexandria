using Alexandria.Business.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess;

public class KoboTrafficLogRepository : IKoboTrafficLogRepository
{
    private readonly string connectionString;

    public KoboTrafficLogRepository(string dbPath = "kobo_traffic.db")
    {
        connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS TrafficLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Method TEXT NOT NULL,
                Url TEXT NOT NULL,
                RequestHeaders TEXT,
                RequestBody TEXT,
                StatusCode INTEGER NOT NULL,
                ResponseHeaders TEXT,
                ResponseBody TEXT,
                DurationMs INTEGER NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task Log(string method, string url, string requestHeaders, string requestBody, int statusCode, string responseHeaders, string responseBody, long durationMs)
    {
        await using var connection = new SqliteConnection(connectionString);
        connection.Open();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TrafficLogs 
                (Timestamp, Method, Url, RequestHeaders, RequestBody,
                 StatusCode, ResponseHeaders, ResponseBody, DurationMs)
            VALUES 
                (@ts, @method, @url, @reqHeaders, @reqBody,
                 @status, @resHeaders, @resBody, @duration)
            """;

        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@method", method);
        cmd.Parameters.AddWithValue("@url", url);
        cmd.Parameters.AddWithValue("@reqHeaders", requestHeaders);
        cmd.Parameters.AddWithValue("@reqBody", requestBody);
        cmd.Parameters.AddWithValue("@status", statusCode);
        cmd.Parameters.AddWithValue("@resHeaders", responseHeaders);
        cmd.Parameters.AddWithValue("@resBody", responseBody);
        cmd.Parameters.AddWithValue("@duration", durationMs);

        await cmd.ExecuteNonQueryAsync();
    }
}