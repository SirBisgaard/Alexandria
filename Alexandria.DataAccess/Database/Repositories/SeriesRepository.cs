using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class SeriesRepository(ISqliteConnectionProvider connectionProvider) : ISeriesRepository
{
    public Task<List<Series>> GetAll()
    {
        return Task.FromResult(connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT SeriesUuid, Name, CreatedAt, ModifiedAt FROM Series ORDER BY Name";

            using var reader = cmd.ExecuteReader();
            var series = new List<Series>();
            while (reader.Read())
            {
                series.Add(MapSeries(reader));
            }

            return series;
        }));
    }

    public Task<Series?> FindByUuid(Guid seriesUuid)
    {
        return Task.FromResult(connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT SeriesUuid, Name, CreatedAt, ModifiedAt FROM Series WHERE SeriesUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", seriesUuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapSeries(reader);

            return (Series?)null;
        }));
    }

    public Task<Guid> GetOrCreate(string name)
    {
        return Task.FromResult(connectionProvider.Write(connection =>
        {
            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT SeriesUuid FROM Series WHERE Name = @name COLLATE NOCASE";
            selectCmd.Parameters.AddWithValue("@name", name);

            var existing = selectCmd.ExecuteScalar();
            if (existing is string uuidStr)
                return Guid.Parse(uuidStr);

            var now = DateTimeOffset.UtcNow.ToString("O");
            var newUuid = Guid.GenerateUuidV5(name.ToLowerInvariant().Trim());
            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO Series (SeriesUuid, Name, CreatedAt, ModifiedAt) VALUES (@uuid, @name, @createdAt, @modifiedAt)";
            insertCmd.Parameters.AddWithValue("@uuid", newUuid.ToString());
            insertCmd.Parameters.AddWithValue("@name", name);
            insertCmd.Parameters.AddWithValue("@createdAt", now);
            insertCmd.Parameters.AddWithValue("@modifiedAt", now);
            insertCmd.ExecuteNonQuery();

            return newUuid;
        }));
    }

    public Task SetBookSeries(Guid bookUuid, Guid seriesUuid, decimal? index)
    {
        connectionProvider.Write(connection =>
        {
            using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM BookSeries WHERE BookUuid = @bookUuid";
            deleteCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            deleteCmd.ExecuteNonQuery();

            using var insertCmd = connection.CreateCommand();
            var now = DateTimeOffset.UtcNow.ToString("O");
            insertCmd.CommandText = "INSERT INTO BookSeries (BookUuid, SeriesUuid, [Index], CreatedAt, ModifiedAt) VALUES (@bookUuid, @seriesUuid, @index, @createdAt, @modifiedAt)";
            insertCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            insertCmd.Parameters.AddWithValue("@seriesUuid", seriesUuid.ToString());
            insertCmd.Parameters.AddWithValue("@index", (object?)index ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@createdAt", now);
            insertCmd.Parameters.AddWithValue("@modifiedAt", now);
            insertCmd.ExecuteNonQuery();
        });
        return Task.CompletedTask;
    }

    public Task RemoveBookSeries(Guid bookUuid)
    {
        connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM BookSeries WHERE BookUuid = @bookUuid";
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            cmd.ExecuteNonQuery();
        });
        return Task.CompletedTask;
    }

    public Task<Series?> GetSeriesByBookUuid(Guid bookUuid)
    {
        return Task.FromResult(connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT s.SeriesUuid, s.Name, bs.[Index], s.CreatedAt, s.ModifiedAt
                FROM Series s
                JOIN BookSeries bs ON s.SeriesUuid = bs.SeriesUuid
                WHERE bs.BookUuid = @bookUuid
                """;
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (Series?)new Series
                {
                    SeriesUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("SeriesUuid"))),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Index = reader.IsDBNull(reader.GetOrdinal("Index")) ? null : reader.GetDecimal(reader.GetOrdinal("Index")),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                    ModifiedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ModifiedAt")))
                };
            }

            return (Series?)null;
        }));
    }

    public Task RemoveOrphanedSeries()
    {
        connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM Series
                WHERE SeriesUuid NOT IN (SELECT DISTINCT SeriesUuid FROM BookSeries)
                """;
            cmd.ExecuteNonQuery();
        });
        return Task.CompletedTask;
    }

    public Task<Series?> FindByName(string name)
    {
        return Task.FromResult(connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT SeriesUuid, Name, CreatedAt, ModifiedAt FROM Series WHERE Name = @name COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@name", name);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapSeries(reader);

            return (Series?)null;
        }));
    }

    public Task Update(Series series)
    {
        connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Series SET Name = @name, ModifiedAt = @modifiedAt WHERE SeriesUuid = @uuid";
            cmd.Parameters.AddWithValue("@name", series.Name);
            cmd.Parameters.AddWithValue("@uuid", series.SeriesUuid.ToString());
            cmd.Parameters.AddWithValue("@modifiedAt", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });
        return Task.CompletedTask;
    }

    public Task ReassignBooks(Guid sourceUuid, Guid targetUuid)
    {
        connectionProvider.Write(connection =>
        {
            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = "UPDATE BookSeries SET SeriesUuid = @targetUuid, ModifiedAt = @modifiedAt WHERE SeriesUuid = @sourceUuid AND BookUuid NOT IN (SELECT BookUuid FROM BookSeries WHERE SeriesUuid = @targetUuid)";
            updateCmd.Parameters.AddWithValue("@sourceUuid", sourceUuid.ToString());
            updateCmd.Parameters.AddWithValue("@targetUuid", targetUuid.ToString());
            updateCmd.Parameters.AddWithValue("@modifiedAt", DateTimeOffset.UtcNow.ToString("O"));
            updateCmd.ExecuteNonQuery();

            using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM BookSeries WHERE SeriesUuid = @sourceUuid";
            deleteCmd.Parameters.AddWithValue("@sourceUuid", sourceUuid.ToString());
            deleteCmd.ExecuteNonQuery();
        });
        return Task.CompletedTask;
    }

    private static Series MapSeries(SqliteDataReader reader)
    {
        return new Series
        {
            SeriesUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("SeriesUuid"))),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ModifiedAt")))
        };
    }
}
