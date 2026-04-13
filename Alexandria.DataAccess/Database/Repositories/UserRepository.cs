using System.Security.Cryptography;
using System.Text;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.DataAccess.Database.Repositories;

public class UserRepository(ISqliteConnectionProvider connectionProvider) : IUserRepository
{
    public async Task<User?> GetByUsername(string username)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT UserUuid, Username, Password, DisplayName, CreatedAt, ModifiedAt, LastLoginAt FROM Users WHERE Username = @username";
            cmd.Parameters.AddWithValue("@username", username.ToLowerInvariant());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return MapUser(reader);
        });
    }

    public async Task<List<User>> GetAll()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT UserUuid, Username, Password, DisplayName, CreatedAt, ModifiedAt, LastLoginAt FROM Users";

            using var reader = cmd.ExecuteReader();
            var result = new List<User>();
            while (reader.Read())
                result.Add(MapUser(reader));

            return result;
        });
    }

    public async Task<User?> GetByUuid(Guid userUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT UserUuid, Username, Password, DisplayName, CreatedAt, ModifiedAt, LastLoginAt FROM Users WHERE UserUuid = @userUuid";
            cmd.Parameters.AddWithValue("@userUuid", userUuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return MapUser(reader);
        });
    }

    public async Task<int> Count()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });
    }

    public async Task Add(User user)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Users (UserUuid, Username, Password, DisplayName, CreatedAt, ModifiedAt, LastLoginAt)
                VALUES (@userUuid, @username, @password, @displayName, @createdAt, @modifiedAt, @lastLoginAt)
                """;
            cmd.Parameters.AddWithValue("@userUuid", user.UserUuid.ToString());
            cmd.Parameters.AddWithValue("@username", user.Username.ToLowerInvariant());
            cmd.Parameters.AddWithValue("@password", user.Password);
            cmd.Parameters.AddWithValue("@displayName", user.DisplayName);
            cmd.Parameters.AddWithValue("@createdAt", now);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.Parameters.AddWithValue("@lastLoginAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task Update(User user)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                UPDATE Users SET Username = @username, Password = @password, DisplayName = @displayName, ModifiedAt = @modifiedAt
                WHERE UserUuid = @userUuid
                """;
            cmd.Parameters.AddWithValue("@userUuid", user.UserUuid.ToString());
            cmd.Parameters.AddWithValue("@username", user.Username.ToLowerInvariant());
            cmd.Parameters.AddWithValue("@password", user.Password);
            cmd.Parameters.AddWithValue("@displayName", user.DisplayName);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task Delete(Guid userUuid)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Users WHERE UserUuid = @userUuid";
            cmd.Parameters.AddWithValue("@userUuid", userUuid.ToString());
            cmd.ExecuteNonQuery();
        });
    }

    public async Task UpdateLastLogin(Guid userUuid)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Users SET LastLoginAt = @lastLoginAt, ModifiedAt = @modifiedAt WHERE UserUuid = @userUuid";
            cmd.Parameters.AddWithValue("@userUuid", userUuid.ToString());
            cmd.Parameters.AddWithValue("@lastLoginAt", now);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public static string HashPassword(string password, Guid salt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salt + password));
        return Convert.ToHexStringLower(bytes);
    }

    private static User MapUser(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new User
        {
            UserUuid = Guid.Parse(reader.GetString(0)),
            Username = reader.GetString(1),
            Password = reader.GetString(2),
            DisplayName = reader.GetString(3),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(4)),
            ModifiedAt = DateTimeOffset.Parse(reader.GetString(5)),
            LastLoginAt = DateTimeOffset.Parse(reader.GetString(6))
        };
    }
}
