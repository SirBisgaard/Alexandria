using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.KoboStore;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class ReadingStateRepository(AlexandriaSettings settings) : IReadingStateRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task<ReadingState?> GetByBookUuid(Guid bookUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM ReadingStates WHERE BookUuid = @bookUuid";
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return MapReadingState(reader);
    }

    public async Task<ReadingProgress?> GetReadingProgress(Guid bookUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT StatusInfoStatus, ProgressPercent, SpentReadingMinutes, RemainingTimeMinutes, CreatedAt, ModifiedAt FROM ReadingStates WHERE BookUuid = @bookUuid";
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ReadingProgress
        {
            Status = Enum.Parse<ReadingStatus>(reader.GetString(reader.GetOrdinal("StatusInfoStatus"))),
            ProgressPercent = reader.IsDBNull(reader.GetOrdinal("ProgressPercent")) ? null : reader.GetDecimal(reader.GetOrdinal("ProgressPercent")),
            SpentReadingMinutes = reader.IsDBNull(reader.GetOrdinal("SpentReadingMinutes")) ? null : reader.GetInt32(reader.GetOrdinal("SpentReadingMinutes")),
            RemainingTimeMinutes = reader.IsDBNull(reader.GetOrdinal("RemainingTimeMinutes")) ? null : reader.GetInt32(reader.GetOrdinal("RemainingTimeMinutes")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ModifiedAt")))
        };
    }

    public async Task Upsert(Guid bookUuid, ReadingState state)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ReadingStates (BookUuid, StatusInfoStatus, StatusInfoLastModified, TimesStartedReading, LastTimeStartedReading, SpentReadingMinutes, RemainingTimeMinutes, StatisticsLastModified, ProgressPercent, ContentSourceProgressPercent, LocationValue, LocationType, LocationSource, BookmarkLastModified, LastModified, PriorityTimestamp, Created, CreatedAt, ModifiedAt)
            VALUES (@bookUuid, @statusInfoStatus, @statusInfoLastModified, @timesStartedReading, @lastTimeStartedReading, @spentReadingMinutes, @remainingTimeMinutes, @statisticsLastModified, @progressPercent, @contentSourceProgressPercent, @locationValue, @locationType, @locationSource, @bookmarkLastModified, @lastModified, @priorityTimestamp, @created, @createdAt, @modifiedAt)
            ON CONFLICT (BookUuid) DO UPDATE SET StatusInfoStatus = @statusInfoStatus, StatusInfoLastModified = @statusInfoLastModified, TimesStartedReading = @timesStartedReading, LastTimeStartedReading = @lastTimeStartedReading, SpentReadingMinutes = @spentReadingMinutes, RemainingTimeMinutes = @remainingTimeMinutes, StatisticsLastModified = @statisticsLastModified, ProgressPercent = @progressPercent, ContentSourceProgressPercent = @contentSourceProgressPercent, LocationValue = @locationValue, LocationType = @locationType, LocationSource = @locationSource, BookmarkLastModified = @bookmarkLastModified, LastModified = @lastModified, PriorityTimestamp = @priorityTimestamp, Created = @created, ModifiedAt = @modifiedAt
            """;

        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        cmd.Parameters.AddWithValue("@statusInfoStatus", state.StatusInfo.Status);
        cmd.Parameters.AddWithValue("@statusInfoLastModified", state.StatusInfo.LastModified);
        cmd.Parameters.AddWithValue("@timesStartedReading", state.StatusInfo.TimesStartedReading);
        cmd.Parameters.AddWithValue("@lastTimeStartedReading", (object?)state.StatusInfo.LastTimeStartedReading ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@spentReadingMinutes", (object?)state.Statistics.SpentReadingMinutes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@remainingTimeMinutes", (object?)state.Statistics.RemainingTimeMinutes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@statisticsLastModified", state.Statistics.LastModified);
        cmd.Parameters.AddWithValue("@progressPercent", ToDecimalOrNull(state.CurrentBookmark.ProgressPercent));
        cmd.Parameters.AddWithValue("@contentSourceProgressPercent", ToDecimalOrNull(state.CurrentBookmark.ContentSourceProgressPercent));
        cmd.Parameters.AddWithValue("@locationValue", (object?)state.CurrentBookmark.Location?.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@locationType", (object?)state.CurrentBookmark.Location?.Type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@locationSource", (object?)state.CurrentBookmark.Location?.Source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bookmarkLastModified", state.CurrentBookmark.LastModified);
        cmd.Parameters.AddWithValue("@lastModified", state.LastModified);
        cmd.Parameters.AddWithValue("@priorityTimestamp", state.PriorityTimestamp);
        cmd.Parameters.AddWithValue("@created", state.Created);
        var now = DateTimeOffset.UtcNow.ToString("O");
        cmd.Parameters.AddWithValue("@createdAt", now);
        cmd.Parameters.AddWithValue("@modifiedAt", now);

        await cmd.ExecuteNonQueryAsync();
    }

    private static ReadingState MapReadingState(SqliteDataReader reader)
    {
        BookmarkLocation? location = null;
        var locationValue = reader.IsDBNull(reader.GetOrdinal("LocationValue")) ? null : reader.GetString(reader.GetOrdinal("LocationValue"));
        if (locationValue is not null)
        {
            location = new BookmarkLocation
            {
                Value = locationValue,
                Type = reader.GetString(reader.GetOrdinal("LocationType")),
                Source = reader.GetString(reader.GetOrdinal("LocationSource"))
            };
        }

        return new ReadingState
        {
            EntitlementId = reader.GetString(reader.GetOrdinal("BookUuid")),
            Created = reader.GetString(reader.GetOrdinal("Created")),
            LastModified = reader.GetString(reader.GetOrdinal("LastModified")),
            PriorityTimestamp = reader.GetString(reader.GetOrdinal("PriorityTimestamp")),
            StatusInfo = new StatusInfo
            {
                LastModified = reader.GetString(reader.GetOrdinal("StatusInfoLastModified")),
                Status = reader.GetString(reader.GetOrdinal("StatusInfoStatus")),
                TimesStartedReading = reader.GetInt32(reader.GetOrdinal("TimesStartedReading")),
                LastTimeStartedReading = reader.IsDBNull(reader.GetOrdinal("LastTimeStartedReading")) ? null : reader.GetString(reader.GetOrdinal("LastTimeStartedReading"))
            },
            Statistics = new Statistics
            {
                LastModified = reader.GetString(reader.GetOrdinal("StatisticsLastModified")),
                SpentReadingMinutes = reader.IsDBNull(reader.GetOrdinal("SpentReadingMinutes")) ? null : reader.GetInt32(reader.GetOrdinal("SpentReadingMinutes")),
                RemainingTimeMinutes = reader.IsDBNull(reader.GetOrdinal("RemainingTimeMinutes")) ? null : reader.GetInt32(reader.GetOrdinal("RemainingTimeMinutes"))
            },
            CurrentBookmark = new Bookmark
            {
                LastModified = reader.GetString(reader.GetOrdinal("BookmarkLastModified")),
                ProgressPercent = reader.IsDBNull(reader.GetOrdinal("ProgressPercent")) ? null : CleanProgress(reader.GetDecimal(reader.GetOrdinal("ProgressPercent"))),
                ContentSourceProgressPercent = reader.IsDBNull(reader.GetOrdinal("ContentSourceProgressPercent")) ? null : CleanProgress(reader.GetDecimal(reader.GetOrdinal("ContentSourceProgressPercent"))),
                Location = location
            }
        };
    }

    private static object ToDecimalOrNull(object? value)
    {
        if (value is null)
        {
            return DBNull.Value;
        }

        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.TryGetDecimal(out var d) ? d : DBNull.Value;
        }

        return Convert.ToDecimal(value);
    }

    private static object CleanProgress(decimal value)
    {
        if (value == Math.Floor(value))
        {
            return (int)value;
        }
        return value;
    }
}
