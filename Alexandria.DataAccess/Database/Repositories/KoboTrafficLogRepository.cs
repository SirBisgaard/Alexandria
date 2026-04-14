using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.DataAccess.Database.Repositories;

public class KoboTrafficLogRepository(ISqliteConnectionProvider connectionProvider) : IKoboTrafficLogRepository
{
    public async Task Log(KoboProxyTrafficLog entry)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
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

            cmd.ExecuteNonQuery();
        });
    }

    public async Task Clear()
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM KoboTrafficLogs";
            cmd.ExecuteNonQuery();
        });
    }
}
