using Alexandria.Business.Extensions;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class AuthorRepository(AlexandriaSettings settings) : IAuthorRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task<List<Author>> GetAll()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT AuthorUuid, Name FROM Authors ORDER BY Name";

        await using var reader = await cmd.ExecuteReaderAsync();
        var authors = new List<Author>();
        while (await reader.ReadAsync())
        {
            authors.Add(MapAuthor(reader));
        }

        return authors;
    }

    public async Task<Author?> FindByUuid(Guid authorUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT AuthorUuid, Name FROM Authors WHERE AuthorUuid = @uuid";
        cmd.Parameters.AddWithValue("@uuid", authorUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return MapAuthor(reader);

        return null;
    }

    public async Task<Author?> FindByName(string name)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT AuthorUuid, Name FROM Authors WHERE Name = @name COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@name", name);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapAuthor(reader);
        }

        return null;
    }

    public async Task<Guid> GetOrCreate(string name)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT AuthorUuid FROM Authors WHERE Name = @name COLLATE NOCASE";
        selectCmd.Parameters.AddWithValue("@name", name);

        var existing = await selectCmd.ExecuteScalarAsync();
        if (existing is string uuidStr)
        {
            return Guid.Parse(uuidStr);
        }

        var newUuid = Guid.GenerateUuidV5(name.ToLowerInvariant().Trim());
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Authors (AuthorUuid, Name) VALUES (@uuid, @name)";
        insertCmd.Parameters.AddWithValue("@uuid", newUuid.ToString());
        insertCmd.Parameters.AddWithValue("@name", name);
        await insertCmd.ExecuteNonQueryAsync();

        return newUuid;
    }

    public async Task SetBookAuthors(Guid bookUuid, List<string> authorNames)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM BookAuthors WHERE BookUuid = @bookUuid";
        deleteCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
        await deleteCmd.ExecuteNonQueryAsync();

        for (var i = 0; i < authorNames.Count; i++)
        {
            var authorUuid = await GetOrCreateWithConnection(connection, authorNames[i]);

            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO BookAuthors (BookUuid, AuthorUuid) VALUES (@bookUuid, @authorUuid)";
            insertCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            insertCmd.Parameters.AddWithValue("@authorUuid", authorUuid.ToString());
            await insertCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT a.AuthorUuid, a.Name
            FROM Authors a
            JOIN BookAuthors ba ON a.AuthorUuid = ba.AuthorUuid
            WHERE ba.BookUuid = @bookUuid
            ORDER BY a.Name
            """;
        cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());

        await using var reader = await cmd.ExecuteReaderAsync();
        var authors = new List<Author>();
        while (await reader.ReadAsync())
        {
            authors.Add(MapAuthor(reader));
        }

        return authors;
    }

    public async Task RemoveOrphanedAuthors()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM Authors
            WHERE AuthorUuid NOT IN (SELECT DISTINCT AuthorUuid FROM BookAuthors)
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> GetOrCreateWithConnection(SqliteConnection connection, string name)
    {
        await using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT AuthorUuid FROM Authors WHERE Name = @name COLLATE NOCASE";
        selectCmd.Parameters.AddWithValue("@name", name);

        var existing = await selectCmd.ExecuteScalarAsync();
        if (existing is string uuidStr)
        {
            return Guid.Parse(uuidStr);
        }

        var newUuid = Guid.GenerateUuidV5(name.ToLowerInvariant().Trim());
        await using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Authors (AuthorUuid, Name) VALUES (@uuid, @name)";
        insertCmd.Parameters.AddWithValue("@uuid", newUuid.ToString());
        insertCmd.Parameters.AddWithValue("@name", name);
        await insertCmd.ExecuteNonQueryAsync();

        return newUuid;
    }

    public async Task Update(Guid authorUuid, string newName)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Authors SET Name = @name WHERE AuthorUuid = @uuid";
        cmd.Parameters.AddWithValue("@name", newName);
        cmd.Parameters.AddWithValue("@uuid", authorUuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static Author MapAuthor(SqliteDataReader reader)
    {
        return new Author
        {
            AuthorUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("AuthorUuid"))),
            Name = reader.GetString(reader.GetOrdinal("Name"))
        };
    }
}
