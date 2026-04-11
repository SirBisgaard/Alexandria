using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database;

public class SqliteConnectionProvider(AlexandriaSettings settings) : IDatabasePersistence, IDisposable
{
    private readonly SqliteConnection connection = CreateAndLoad(settings.Database);
    private readonly string diskPath = settings.Database;
    private readonly SemaphoreSlim flushLock = new(1, 1);

    public SqliteConnection Connection => connection;

    public Task Flush() => FlushToDisk();

    public async Task FlushToDisk()
    {
        await flushLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
            await Task.Run(() =>
            {
                using var diskConn = new SqliteConnection($"Data Source={diskPath}");
                diskConn.Open();
                connection.BackupDatabase(diskConn);
            });
        }
        finally
        {
            flushLock.Release();
        }
    }

    public void Dispose()
    {
        connection.Dispose();
        flushLock.Dispose();
    }

    private static SqliteConnection CreateAndLoad(string diskPath)
    {
        var memConn = new SqliteConnection("Data Source=:memory:");
        memConn.Open();

        if (File.Exists(diskPath))
        {
            using var diskConn = new SqliteConnection($"Data Source={diskPath}");
            diskConn.Open();
            diskConn.BackupDatabase(memConn);
        }

        return memConn;
    }
}
