using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Database.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database;

public class SqliteConnectionProvider : ISqliteConnectionProvider, IDisposable
{
    private bool isLastWriteFlush;
    private readonly string diskPath;
    private readonly SqliteConnection connection;
    private readonly SemaphoreSlim dbLock = new(1, 1);

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

    public async Task<T> Read<T>(Func<SqliteConnection, T> operation)
    {
        await dbLock.WaitAsync();
        try
        {
            isLastWriteFlush = false;
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
            isLastWriteFlush = false;
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
            isLastWriteFlush = false;
            return operation(connection);
        }
        finally
        {
            dbLock.Release();
        }
    }

    public async Task<bool> Flush()
    {
        await dbLock.WaitAsync();
        try
        {
            if (isLastWriteFlush)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
            await using var diskConn = new SqliteConnection($"Data Source={diskPath}");
            diskConn.Open();
            connection.BackupDatabase(diskConn);
            return true;
        }
        finally
        {
            isLastWriteFlush = true;
            dbLock.Release();
        }
    }

    public void Dispose()
    {
        connection.Dispose();
        dbLock.Dispose();
    }
}