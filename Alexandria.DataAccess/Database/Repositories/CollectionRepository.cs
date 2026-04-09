using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class CollectionRepository(AlexandriaSettings settings) : ICollectionRepository
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

        var collections = await QueryCollections(connection, "SELECT * FROM Collections WHERE CollectionUuid = @uuid", ("@uuid", uuid.ToString()));
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
        upsertCmd.CommandText = "INSERT OR REPLACE INTO Collections (CollectionUuid, Name, CreatedAt, ModifiedAt) VALUES (@uuid, @name, @createdAt, @modifiedAt)";
        upsertCmd.Parameters.AddWithValue("@uuid", collection.CollectionUuid.ToString());
        upsertCmd.Parameters.AddWithValue("@name", collection.Name);
        upsertCmd.Parameters.AddWithValue("@createdAt", collection.CreatedAt.ToString("O"));
        upsertCmd.Parameters.AddWithValue("@modifiedAt", collection.ModifiedAt.ToString("O"));
        await upsertCmd.ExecuteNonQueryAsync();

        await using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM CollectionBooks WHERE CollectionUuid = @uuid";
        deleteCmd.Parameters.AddWithValue("@uuid", collection.CollectionUuid.ToString());
        await deleteCmd.ExecuteNonQueryAsync();

        foreach (var bookUuid in collection.BookUuids)
        {
            await using var insertCmd = connection.CreateCommand();
            var now = DateTimeOffset.UtcNow.ToString("O");
            insertCmd.CommandText = "INSERT INTO CollectionBooks (CollectionUuid, BookUuid, CreatedAt, ModifiedAt) VALUES (@collectionUuid, @bookUuid, @createdAt, @modifiedAt)";
            insertCmd.Parameters.AddWithValue("@collectionUuid", collection.CollectionUuid.ToString());
            insertCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            insertCmd.Parameters.AddWithValue("@createdAt", now);
            insertCmd.Parameters.AddWithValue("@modifiedAt", now);
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
        deleteCmd.CommandText = "DELETE FROM Collections WHERE CollectionUuid = @uuid";
        deleteCmd.Parameters.AddWithValue("@uuid", uuid.ToString());
        await deleteCmd.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }

    public async Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var collections = await QueryCollections(connection, "SELECT * FROM Collections WHERE ModifiedAt > @since", ("@since", since.ToString("O")));
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
                CollectionUuid = Guid.Parse(reader.GetString(reader.GetOrdinal("CollectionUuid"))),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                ModifiedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ModifiedAt")))
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

        var lookup = collections.ToDictionary(c => c.CollectionUuid);
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
