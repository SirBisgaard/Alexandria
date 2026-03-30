using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
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

    public async Task Log(KoboTrafficLog entry)
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