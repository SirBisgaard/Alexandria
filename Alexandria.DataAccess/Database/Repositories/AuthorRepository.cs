using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class AuthorRepository(ISqliteConnectionProvider connectionProvider) : IAuthorRepository
{
    public async Task<List<Author>> GetAll()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT AuthorUuid, Name, CreatedAt, ModifiedAt FROM Authors ORDER BY Name";

            using var reader = cmd.ExecuteReader();
            var authors = new List<Author>();
            while (reader.Read())
            {
                authors.Add(MapAuthor(reader));
            }

            return authors;
        });
    }

    public async Task<Author?> FindByUuid(Guid authorUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT AuthorUuid, Name, CreatedAt, ModifiedAt FROM Authors WHERE AuthorUuid = @uuid";
            cmd.Parameters.AddWithValue("@uuid", authorUuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapAuthor(reader);

            return null;
        });
    }

    public async Task<Author?> FindByName(string name)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT AuthorUuid, Name, CreatedAt, ModifiedAt FROM Authors WHERE Name = @name COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@name", name);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapAuthor(reader);

            return null;
        });
    }

    public async Task<Guid> GetOrCreate(string name)
    {
        return await connectionProvider.Write(connection =>
        {
            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT AuthorUuid FROM Authors WHERE Name = @name COLLATE NOCASE";
            selectCmd.Parameters.AddWithValue("@name", name);

            var existing = selectCmd.ExecuteScalar();
            if (existing is string uuidStr)
                return Guid.Parse(uuidStr);

            var now = DateTimeOffset.UtcNow.ToString("O");
            var newUuid = Guid.GenerateUuidV5(name.ToLowerInvariant().Trim());
            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO Authors (AuthorUuid, Name, CreatedAt, ModifiedAt) VALUES (@uuid, @name, @createdAt, @modifiedAt)";
            insertCmd.Parameters.AddWithValue("@uuid", newUuid.ToString());
            insertCmd.Parameters.AddWithValue("@name", name);
            insertCmd.Parameters.AddWithValue("@createdAt", now);
            insertCmd.Parameters.AddWithValue("@modifiedAt", now);
            insertCmd.ExecuteNonQuery();

            return newUuid;
        });
    }

    public async Task SetBookAuthors(Guid bookUuid, List<string> authorNames)
    {
        await connectionProvider.Write(connection =>
        {
            using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM BookAuthors WHERE BookUuid = @bookUuid";
            deleteCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            deleteCmd.ExecuteNonQuery();

            var now = DateTimeOffset.UtcNow.ToString("O");
            for (var i = 0; i < authorNames.Count; i++)
            {
                var authorUuid = GetOrCreateWithConnection(connection, authorNames[i]);

                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = "INSERT INTO BookAuthors (BookUuid, AuthorUuid, CreatedAt, ModifiedAt) VALUES (@bookUuid, @authorUuid, @createdAt, @modifiedAt)";
                insertCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
                insertCmd.Parameters.AddWithValue("@authorUuid", authorUuid.ToString());
                insertCmd.Parameters.AddWithValue("@createdAt", now);
                insertCmd.Parameters.AddWithValue("@modifiedAt", now);
                insertCmd.ExecuteNonQuery();
            }
        });
    }

    public async Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT a.AuthorUuid, a.Name, a.CreatedAt, a.ModifiedAt
                FROM Authors a
                JOIN BookAuthors ba ON a.AuthorUuid = ba.AuthorUuid
                WHERE ba.BookUuid = @bookUuid
                ORDER BY a.Name
                """;
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());

            using var reader = cmd.ExecuteReader();
            var authors = new List<Author>();
            while (reader.Read())
            {
                authors.Add(MapAuthor(reader));
            }

            return authors;
        });
    }

    public async Task RemoveOrphanedAuthors()
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM Authors
                WHERE AuthorUuid NOT IN (SELECT DISTINCT AuthorUuid FROM BookAuthors)
                """;
            cmd.ExecuteNonQuery();
        });
    }

    private static Guid GetOrCreateWithConnection(SqliteConnection connection, string name)
    {
        using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT AuthorUuid FROM Authors WHERE Name = @name COLLATE NOCASE";
        selectCmd.Parameters.AddWithValue("@name", name);

        var existing = selectCmd.ExecuteScalar();
        if (existing is string uuidStr)
        {
            return Guid.Parse(uuidStr);
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var newUuid = Guid.GenerateUuidV5(name.ToLowerInvariant().Trim());
        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Authors (AuthorUuid, Name, CreatedAt, ModifiedAt) VALUES (@uuid, @name, @createdAt, @modifiedAt)";
        insertCmd.Parameters.AddWithValue("@uuid", newUuid.ToString());
        insertCmd.Parameters.AddWithValue("@name", name);
        insertCmd.Parameters.AddWithValue("@createdAt", now);
        insertCmd.Parameters.AddWithValue("@modifiedAt", now);
        insertCmd.ExecuteNonQuery();

        return newUuid;
    }

    public async Task Update(Author author)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Authors SET Name = @name, ModifiedAt = @modifiedAt WHERE AuthorUuid = @uuid";
            cmd.Parameters.AddWithValue("@name", author.Name);
            cmd.Parameters.AddWithValue("@uuid", author.AuthorUuid.ToString());
            cmd.Parameters.AddWithValue("@modifiedAt", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        });
    }

    public async Task ReassignBooks(Guid sourceUuid, Guid targetUuid)
    {
        await connectionProvider.Write(connection =>
        {
            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = "UPDATE BookAuthors SET AuthorUuid = @targetUuid, ModifiedAt = @modifiedAt WHERE AuthorUuid = @sourceUuid AND BookUuid NOT IN (SELECT BookUuid FROM BookAuthors WHERE AuthorUuid = @targetUuid)";
            updateCmd.Parameters.AddWithValue("@sourceUuid", sourceUuid.ToString());
            updateCmd.Parameters.AddWithValue("@targetUuid", targetUuid.ToString());
            updateCmd.Parameters.AddWithValue("@modifiedAt", DateTimeOffset.UtcNow.ToString("O"));
            updateCmd.ExecuteNonQuery();

            using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM BookAuthors WHERE AuthorUuid = @sourceUuid";
            deleteCmd.Parameters.AddWithValue("@sourceUuid", sourceUuid.ToString());
            deleteCmd.ExecuteNonQuery();
        });
    }

    private static Author MapAuthor(SqliteDataReader reader)
    {
        return new Author
        {
            AuthorUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("AuthorUuid"))),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ModifiedAt")))
        };
    }
}
