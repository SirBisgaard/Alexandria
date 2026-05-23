using Alexandria.Business.Integrations.Models;
using Alexandria.Business.Interfaces;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.DataAccess.Database.Repositories;

public class AudioBookShelfSyncIntegrationRepository(ISqliteConnectionProvider connectionProvider) : IAudioBookShelfSyncIntegrationRepository
{
    public async Task Upsert(AudioBookShelfIntegration integration)
    {
        await connectionProvider.Write(connection =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO UserIntegrations_AudioBookShelf (UserUuid, Enabled, ServerUrl, ApiToken, CreatedAt, ModifiedAt)
                VALUES (@userUuid, @enabled, @serverUrl, @apiToken, @createdAt, @modifiedAt)
                ON CONFLICT(UserUuid) DO UPDATE SET
                    Enabled = excluded.Enabled,
                    ServerUrl = excluded.ServerUrl,
                    ApiToken = excluded.ApiToken,
                    ModifiedAt = excluded.ModifiedAt
                """;
            cmd.Parameters.AddWithValue("@userUuid", integration.UserUuid.ToString());
            cmd.Parameters.AddWithValue("@enabled", integration.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@serverUrl", integration.ServerUrl);
            cmd.Parameters.AddWithValue("@apiToken", integration.ApiToken);
            cmd.Parameters.AddWithValue("@createdAt", now);
            cmd.Parameters.AddWithValue("@modifiedAt", now);
            cmd.ExecuteNonQuery();
        });
    }

    public async Task<AudioBookShelfIntegration?> GetByUserUuid(Guid userUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT UserUuid, Enabled, ServerUrl, ApiToken, CreatedAt, ModifiedAt, LastSyncedAt FROM UserIntegrations_AudioBookShelf WHERE UserUuid = @userUuid";
            cmd.Parameters.AddWithValue("@userUuid", userUuid.ToString());

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            return new AudioBookShelfIntegration
            {
                UserUuid = Guid.Parse(reader.GetString(0)),
                Enabled = reader.GetInt32(1) == 1,
                ServerUrl = reader.GetString(2),
                ApiToken = reader.GetString(3),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(4)),
                ModifiedAt = DateTimeOffset.Parse(reader.GetString(5)),
                LastSyncedAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6))
            };
        });
    }

    public async Task<List<AudioBookShelfIntegration>> GetAllEnabled()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT UserUuid, Enabled, ServerUrl, ApiToken, CreatedAt, ModifiedAt, LastSyncedAt FROM UserIntegrations_AudioBookShelf WHERE Enabled = 1";

            using var reader = cmd.ExecuteReader();
            var result = new List<AudioBookShelfIntegration>();
            while (reader.Read())
            {
                result.Add(new AudioBookShelfIntegration
                {
                    UserUuid = Guid.Parse(reader.GetString(0)),
                    Enabled = reader.GetInt32(1) == 1,
                    ServerUrl = reader.GetString(2),
                    ApiToken = reader.GetString(3),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(4)),
                    ModifiedAt = DateTimeOffset.Parse(reader.GetString(5)),
                    LastSyncedAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6))
                });
            }
            return result;
        });
    }

    public async Task UpdateLastSynced(Guid userUuid, DateTimeOffset timestamp)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE UserIntegrations_AudioBookShelf SET LastSyncedAt = @lastSyncedAt WHERE UserUuid = @userUuid";
            cmd.Parameters.AddWithValue("@userUuid", userUuid.ToString());
            cmd.Parameters.AddWithValue("@lastSyncedAt", timestamp.ToString("O"));
            cmd.ExecuteNonQuery();
        });
    }

    public async Task<Dictionary<Guid, DateTimeOffset>> GetBookSyncedStates(Guid userUuid)
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT BookUuid, AbsLastUpdate FROM UserIntegrations_AudioBookShelf_BookSyncedState WHERE UserUuid = @userUuid";
            cmd.Parameters.AddWithValue("@userUuid", userUuid.ToString());

            using var reader = cmd.ExecuteReader();
            var result = new Dictionary<Guid, DateTimeOffset>();
            while (reader.Read())
                result[Guid.Parse(reader.GetString(0))] = DateTimeOffset.Parse(reader.GetString(1));
            return result;
        });
    }

    public async Task UpsertBookSyncedState(Guid userUuid, Guid bookUuid, DateTimeOffset absLastUpdate)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO UserIntegrations_AudioBookShelf_BookSyncedState (UserUuid, BookUuid, AbsLastUpdate)
                VALUES (@userUuid, @bookUuid, @absLastUpdate)
                ON CONFLICT(UserUuid, BookUuid) DO UPDATE SET AbsLastUpdate = excluded.AbsLastUpdate
                """;
            cmd.Parameters.AddWithValue("@userUuid", userUuid.ToString());
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            cmd.Parameters.AddWithValue("@absLastUpdate", absLastUpdate.ToString("O"));
            cmd.ExecuteNonQuery();
        });
    }

    public async Task DeleteBookSyncedState(Guid userUuid, Guid bookUuid)
    {
        await connectionProvider.Write(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM UserIntegrations_AudioBookShelf_BookSyncedState WHERE UserUuid = @userUuid AND BookUuid = @bookUuid";
            cmd.Parameters.AddWithValue("@userUuid", userUuid.ToString());
            cmd.Parameters.AddWithValue("@bookUuid", bookUuid.ToString());
            cmd.ExecuteNonQuery();
        });
    }
}
