using Alexandria.Business.Interfaces;
using Microsoft.Data.Sqlite;

namespace Alexandria.DataAccess.Database;

public interface ISqliteConnectionProvider : IDatabasePersistence
{
    T Read<T>(Func<SqliteConnection, T> operation);
    void Write(Action<SqliteConnection> operation);
    T Write<T>(Func<SqliteConnection, T> operation);
}
