using Alexandria.Business.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database.Interfaces;

public interface ISqliteConnectionProvider : IDatabasePersistence
{
    Task<T> Read<T>(Func<SqliteConnection, T> operation);
    Task Write(Action<SqliteConnection> operation);
    Task<T> Write<T>(Func<SqliteConnection, T> operation);
}
