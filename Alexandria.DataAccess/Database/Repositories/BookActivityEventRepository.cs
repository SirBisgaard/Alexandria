using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class BookActivityEventRepository(ISqliteConnectionProvider connectionProvider) : IBookActivityEventRepository
{
    public async Task Insert(BookActivityEvent activityEvent)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BookActivityEvents (EventId, BookUuid, DeviceUuid, EventType, EventTimestamp, Body, ReceivedAt)
                VALUES (@eventId, @bookUuid, @deviceUuid, @eventType, @eventTimestamp, @body, @receivedAt)
                ON CONFLICT (EventId) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("@eventId", activityEvent.EventId);
            cmd.Parameters.AddWithValue("@bookUuid", activityEvent.BookUuid.ToString());
            cmd.Parameters.AddWithValue("@deviceUuid", activityEvent.DeviceUuid);
            cmd.Parameters.AddWithValue("@eventType", activityEvent.EventType);
            cmd.Parameters.AddWithValue("@eventTimestamp", activityEvent.EventTimestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@body", activityEvent.Body);
            cmd.Parameters.AddWithValue("@receivedAt", activityEvent.ReceivedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        });
    }

    public async Task<List<BookActivityEvent>> GetByBookUuid(Guid bookUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT EventId, BookUuid, DeviceUuid, EventType, EventTimestamp, Body, ReceivedAt
                FROM BookActivityEvents
                WHERE BookUuid = @bookUuid
                ORDER BY EventTimestamp DESC
                """;
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());

            using var reader = cmd.ExecuteReader();
            var result = new List<BookActivityEvent>();
            while (reader.Read())
                result.Add(ReadEvent(reader));
            return result;
        });
    }

    public async Task<BookActivityEvent?> GetLatest(Guid bookUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT EventId, BookUuid, DeviceUuid, EventType, EventTimestamp, Body, ReceivedAt
                FROM BookActivityEvents
                WHERE BookUuid = @bookUuid
                ORDER BY EventTimestamp DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadEvent(reader) : null;
        });
    }

    public async Task UpdateTimestampAndBody(string eventId, DateTimeOffset eventTimestamp, string body)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE BookActivityEvents SET EventTimestamp = @eventTimestamp, Body = @body WHERE EventId = @eventId";
            cmd.Parameters.AddWithValue("@eventId", eventId);
            cmd.Parameters.AddWithValue("@eventTimestamp", eventTimestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@body", body);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task<int> DeleteAll()
    {
        return await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM BookActivityEvents";
            return cmd.ExecuteNonQuery();
        });
    }

    private static BookActivityEvent ReadEvent(SqliteDataReader reader) => new()
    {
        EventId = reader.GetString(0),
        BookUuid = Guid.Parse(reader.GetString(1)),
        DeviceUuid = reader.GetString(2),
        EventType = reader.GetString(3),
        EventTimestamp = DateTimeOffset.Parse(reader.GetString(4)),
        Body = reader.GetString(5),
        ReceivedAt = DateTimeOffset.Parse(reader.GetString(6))
    };
}
