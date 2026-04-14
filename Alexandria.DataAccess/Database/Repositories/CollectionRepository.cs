using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Repositories;

public class CollectionRepository(ISqliteConnectionProvider connectionProvider) : ICollectionRepository
{
    public async Task<List<BookCollection>> GetAll()
    {
        return await connectionProvider.Read(connection =>
        {
            var collections = QueryCollections(connection, "SELECT * FROM Collections");
            HydrateBookUuids(connection, collections);
            return collections;
        });
    }

    public async Task<BookCollection?> FindByUuid(Guid uuid)
    {
        return await connectionProvider.Read(connection =>
        {
            var collections = QueryCollections(connection, "SELECT * FROM Collections WHERE CollectionUuid = @uuid", ("@uuid", uuid.ToString()));
            if (collections.Count == 0)
                return (BookCollection?)null;

            HydrateBookUuids(connection, collections);
            return (BookCollection?)collections[0];
        });
    }

    public async Task Upsert(BookCollection collection)
    {
        await connectionProvider.Write(connection =>
        {
            using var transaction = connection.BeginTransaction();

            using var upsertCmd = connection.CreateCommand();
            upsertCmd.CommandText = "INSERT OR REPLACE INTO Collections (CollectionUuid, Name, CreatedAt, ModifiedAt) VALUES (@uuid, @name, @createdAt, @modifiedAt)";
            upsertCmd.Parameters.AddWithValue("@uuid", collection.CollectionUuid.ToString());
            upsertCmd.Parameters.AddWithValue("@name", collection.Name);
            upsertCmd.Parameters.AddWithValue("@createdAt", collection.CreatedAt.ToString("O"));
            upsertCmd.Parameters.AddWithValue("@modifiedAt", collection.ModifiedAt.ToString("O"));
            upsertCmd.ExecuteNonQuery();

            using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM CollectionBooks WHERE CollectionUuid = @uuid";
            deleteCmd.Parameters.AddWithValue("@uuid", collection.CollectionUuid.ToString());
            deleteCmd.ExecuteNonQuery();

            foreach (var bookUuid in collection.BookUuids)
            {
                using var insertCmd = connection.CreateCommand();
                var now = DateTimeOffset.UtcNow.ToString("O");
                insertCmd.CommandText = "INSERT INTO CollectionBooks (CollectionUuid, BookUuid, CreatedAt, ModifiedAt) VALUES (@collectionUuid, @bookUuid, @createdAt, @modifiedAt)";
                insertCmd.Parameters.AddWithValue("@collectionUuid", collection.CollectionUuid.ToString());
                insertCmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
                insertCmd.Parameters.AddWithValue("@createdAt", now);
                insertCmd.Parameters.AddWithValue("@modifiedAt", now);
                insertCmd.ExecuteNonQuery();
            }

            transaction.Commit();
        });
    }

    public async Task Delete(Guid uuid)
    {
        await connectionProvider.Write(connection =>
        {
            using var transaction = connection.BeginTransaction();

            using var deleteBooksCmd = connection.CreateCommand();
            deleteBooksCmd.CommandText = "DELETE FROM CollectionBooks WHERE CollectionUuid = @uuid";
            deleteBooksCmd.Parameters.AddWithValue("@uuid", uuid.ToString());
            deleteBooksCmd.ExecuteNonQuery();

            using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Collections WHERE CollectionUuid = @uuid";
            deleteCmd.Parameters.AddWithValue("@uuid", uuid.ToString());
            deleteCmd.ExecuteNonQuery();

            transaction.Commit();
        });
    }

    public async Task<List<BookCollection>> GetModifiedSince(DateTimeOffset since)
    {
        return await connectionProvider.Read(connection =>
        {
            var collections = QueryCollections(connection, "SELECT * FROM Collections WHERE ModifiedAt > @since", ("@since", since.ToString("O")));
            HydrateBookUuids(connection, collections);
            return collections;
        });
    }

    private static List<BookCollection> QueryCollections(SqliteConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        using var reader = cmd.ExecuteReader();
        var collections = new List<BookCollection>();
        while (reader.Read())
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

    private static void HydrateBookUuids(SqliteConnection connection, List<BookCollection> collections)
    {
        if (collections.Count == 0)
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CollectionUuid, BookUuid FROM CollectionBooks";
        using var reader = cmd.ExecuteReader();

        var lookup = collections.ToDictionary(c => c.CollectionUuid);
        while (reader.Read())
        {
            var collectionUuid = Guid.Parse(reader.GetString(0));
            if (lookup.TryGetValue(collectionUuid, out var collection))
            {
                collection.BookUuids.Add(Guid.Parse(reader.GetString(1)));
            }
        }
    }
}
