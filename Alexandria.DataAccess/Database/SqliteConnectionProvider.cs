using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database;

/// <summary>
/// Owns the single SQLite connection to the database file at <see cref="AlexandriaSettings.Database"/>.
/// The file is opened in WAL mode with synchronous=NORMAL, so every commit is durable on its own —
/// there is no separate flush step. Opening a database created by an older release converts it to
/// WAL mode in place. All access is serialized through one semaphore.
/// </summary>
public class SqliteConnectionProvider : ISqliteConnectionProvider, IDisposable
{
    private readonly SqliteConnection connection;
    private readonly SemaphoreSlim dbLock = new(1, 1);

    public SqliteConnectionProvider(AlexandriaSettings settings)
    {
        connection = new SqliteConnection($"Data Source={settings.Database};Pooling=False");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
    }

    public async Task<T> Read<T>(Func<SqliteConnection, T> operation)
    {
        await dbLock.WaitAsync();
        try
        {
            return operation(connection);
        }
        finally
        {
            dbLock.Release();
        }
    }

    public async Task Write(Action<SqliteConnection> operation)
    {
        await dbLock.WaitAsync();
        try
        {
            operation(connection);
        }
        finally
        {
            dbLock.Release();
        }
    }

    public async Task<T> Write<T>(Func<SqliteConnection, T> operation)
    {
        await dbLock.WaitAsync();
        try
        {
            return operation(connection);
        }
        finally
        {
            dbLock.Release();
        }
    }

    public void Dispose()
    {
        connection.Dispose();
        dbLock.Dispose();
    }
}
