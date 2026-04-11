using Alexandria.Business.Library.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database;

public class SqliteConnectionProvider : ISqliteConnectionProvider, IDisposable
{
    private readonly string diskPath;
    private readonly SqliteConnection connection;
    private readonly ReaderWriterLockSlim rwLock = new();

    public SqliteConnectionProvider(AlexandriaSettings settings)
    {
        diskPath = settings.Database;
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        if (!File.Exists(diskPath))
            return;
        
        using var diskConn = new SqliteConnection($"Data Source={diskPath}");
        diskConn.Open();
        diskConn.BackupDatabase(connection);
    }

    public T Read<T>(Func<SqliteConnection, T> operation)
    {
        rwLock.EnterReadLock();
        try
        {
            return operation(connection);
        }
        finally
        {
            rwLock.ExitReadLock();
        }
    }

    public void Write(Action<SqliteConnection> operation)
    {
        rwLock.EnterWriteLock();
        try
        {
            operation(connection);
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }

    public T Write<T>(Func<SqliteConnection, T> operation)
    {
        rwLock.EnterWriteLock();
        try
        {
            return operation(connection);
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }

    public Task Flush()
    {
        rwLock.EnterWriteLock();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
            using var diskConn = new SqliteConnection($"Data Source={diskPath}");
            diskConn.Open();
            connection.BackupDatabase(diskConn);
            return Task.CompletedTask;
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        connection.Dispose();
        rwLock.Dispose();
    }
}