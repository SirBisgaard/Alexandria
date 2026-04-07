using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class SeriesRepository(AlexandriaSettings settings) : ISeriesRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task<List<Series>> GetAll()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT SeriesUuid, Name FROM Series ORDER BY Name";

        await using var reader = await cmd.ExecuteReaderAsync();
        var series = new List<Series>();
        while (await reader.ReadAsync())
        {
            series.Add(MapSeries(reader));
        }

        return series;
    }

    public async Task<Series?> FindByUuid(Guid seriesUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT SeriesUuid, Name FROM Series WHERE SeriesUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", seriesUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapSeries(reader);
        }

        return null;
    }

    public async Task<Guid> GetOrCreate(string name)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT SeriesUuid FROM Series WHERE Name = @name COLLATE NOCASE";
        selectCmd.Parameters.AddWithValue("@name", name);

        var existing = await selectCmd.ExecuteScalarAsync();
        if (existing is string uuidStr)
        {
            return Guid.Parse(uuidStr);
        }

        var newUuid = Guid.GenerateUuidV5(name.ToLowerInvariant().Trim());
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Series (SeriesUuid, Name) VALUES (@uuid, @name)";
        insertCmd.Parameters.AddWithValue("@uuid", newUuid.ToString());
        insertCmd.Parameters.AddWithValue("@name", name);
        await insertCmd.ExecuteNonQueryAsync();

        return newUuid;
    }

    public async Task SetBookSeries(Guid bookUuid, Guid seriesUuid, decimal? index)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM BookSeries WHERE BookUuid = @bookUuid";
        deleteCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        await deleteCmd.ExecuteNonQueryAsync();

        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO BookSeries (BookUuid, SeriesUuid, [Index]) VALUES (@bookUuid, @seriesUuid, @index)";
        insertCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        insertCmd.Parameters.AddWithValue("@seriesUuid", seriesUuid.ToString());
        insertCmd.Parameters.AddWithValue("@index", (object?)index ?? DBNull.Value);
        await insertCmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveBookSeries(Guid bookUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM BookSeries WHERE BookUuid = @bookUuid";
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Series?> GetSeriesByBookUuid(Guid bookUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.SeriesUuid, s.Name, bs.[Index]
            FROM Series s
            JOIN BookSeries bs ON s.SeriesUuid = bs.SeriesUuid
            WHERE bs.BookUuid = @bookUuid
            """;
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Series
            {
                SeriesUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("SeriesUuid"))),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Index = reader.IsDBNull(reader.GetOrdinal("Index")) ? null : reader.GetDecimal(reader.GetOrdinal("Index"))
            };
        }

        return null;
    }

    public async Task RemoveOrphanedSeries()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM Series
            WHERE SeriesUuid NOT IN (SELECT DISTINCT SeriesUuid FROM BookSeries)
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Series?> FindByName(string name)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT SeriesUuid, Name FROM Series WHERE Name = @name COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@name", name);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return MapSeries(reader);

        return null;
    }

    public async Task Update(Series series)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Series SET Name = @name WHERE SeriesUuid = @uuid";
        cmd.Parameters.AddWithValue("@name", series.Name);
        cmd.Parameters.AddWithValue("@uuid", series.SeriesUuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static Series MapSeries(SqliteDataReader reader)
    {
        return new Series
        {
            SeriesUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("SeriesUuid"))),
            Name = reader.GetString(reader.GetOrdinal("Name"))
        };
    }
}
