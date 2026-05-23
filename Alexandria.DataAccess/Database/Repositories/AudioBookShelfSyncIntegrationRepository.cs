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
}
