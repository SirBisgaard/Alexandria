using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Repositories;

public class BookCollectionRepository(AlexandriaSettings settings) : IBookCollectionRepository
{
    private readonly string connectionString = $"Data Source={settings.Database}";

    public async Task<List<BookCollection>> GetAll()
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var collections = await QueryCollections(connection, "SELECT * FROM Collections");
        await HydrateBookUuids(connection, collections);
        return collections;
    }

    public async Task<BookCollection?> FindByUuid(Guid uuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var collections = await QueryCollections(connection, "SELECT * FROM Collections WHERE Uuid = @uuid", ("@uuid", uuid.ToString()));
        if (collections.Count == 0)
        {
            return null;
        }

        await HydrateBookUuids(connection, collections);
        return collections[0];
    }

    public async Task Upsert(BookCollection collection)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using var upsertCmd = connection.CreateCommand();
        upsertCmd.CommandText = "INSERT OR REPLACE INTO Collections (Uuid, Name, CreatedAt, LastModified) VALUES (@uuid, @name, @createdAt, @lastModified)";
        upsertCmd.Parameters.AddWithValue("@uuid", collection.Uuid.ToString());
        upsertCmd.Parameters.AddWithValue("@name", collection.Name);
        upsertCmd.Parameters.AddWithValue("@createdAt", collection.CreatedAt.ToString("O"));
        upsertCmd.Parameters.AddWithValue("@lastModified", collection.LastModified.ToString("O"));
        await upsertCmd.ExecuteNonQueryAsync();

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM CollectionBooks WHERE CollectionUuid = @uuid";
        deleteCmd.Parameters.AddWithValue("@uuid", collection.Uuid.ToString());
        await deleteCmd.ExecuteNonQueryAsync();

        foreach (var bookUuid in collection.BookUuids)
        {
            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO CollectionBooks (CollectionUuid, BookUuid) VALUES (@collectionUuid, @bookUuid)";
            insertCmd.Parameters.AddWithValue("@collectionUuid", collection.Uuid.ToString());
            insertCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            await insertCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task Delete(Guid uuid)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using var deleteBooksCmd = connection.CreateCommand();
        deleteBooksCmd.CommandText = "DELETE FROM CollectionBooks WHERE CollectionUuid = @uuid";
        deleteBooksCmd.Parameters.AddWithValue("@uuid", uuid.ToString());
        await deleteBooksCmd.ExecuteNonQueryAsync();

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM Collections WHERE Uuid = @uuid";
        deleteCmd.Parameters.AddWithValue("@uuid", uuid.ToString());
        await deleteCmd.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }

    public async Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var collections = await QueryCollections(connection, "SELECT * FROM Collections WHERE LastModified > @since", ("@since", since.ToString("O")));
        await HydrateBookUuids(connection, collections);
        return collections;
    }

    private static async Task<List<BookCollection>> QueryCollections(SqliteConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        var collections = new List<BookCollection>();
        while (await reader.ReadAsync())
        {
            collections.Add(new BookCollection
            {
                Uuid = Guid.Parse(reader.GetString(reader.GetOrdinal("Uuid"))),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                LastModified = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LastModified")))
            });
        }

        return collections;
    }

    private static async Task HydrateBookUuids(SqliteConnection connection, List<BookCollection> collections)
    {
        if (collections.Count == 0)
        {
            return;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CollectionUuid, BookUuid FROM CollectionBooks";
        await using var reader = await cmd.ExecuteReaderAsync();

        var lookup = collections.ToDictionary(c => c.Uuid);
        while (await reader.ReadAsync())
        {
            var collectionUuid = Guid.Parse(reader.GetString(0));
            if (lookup.TryGetValue(collectionUuid, out var collection))
            {
                collection.BookUuids.Add(Guid.Parse(reader.GetString(1)));
            }
        }
    }
}
