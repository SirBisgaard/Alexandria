using System.Security.Cryptography;
using System.Text;
using Alexandria.Business.Interfaces;
using Alexandria.DataAccess.Database.Interfaces;

namespace Alexandria.DataAccess.Database.Repositories;

public class LibraryVersionRepository(ISqliteConnectionProvider connectionProvider) : ILibraryVersionRepository
{
    public async Task<string> GetVersionToken()
    {
        return await connectionProvider.Read(connection =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM Books), (SELECT MAX(ModifiedAt) FROM Books),
                    (SELECT COUNT(*) FROM Authors), (SELECT MAX(ModifiedAt) FROM Authors),
                    (SELECT COUNT(*) FROM Series), (SELECT MAX(ModifiedAt) FROM Series),
                    (SELECT COUNT(*) FROM KoboReadingStates), (SELECT MAX(ModifiedAt) FROM KoboReadingStates),
                    (SELECT COUNT(*) FROM Collections), (SELECT MAX(ModifiedAt) FROM Collections),
                    (SELECT COUNT(*) FROM CollectionBooks), (SELECT MAX(ModifiedAt) FROM CollectionBooks),
                    (SELECT COUNT(*) FROM BookActivityEvents), (SELECT MAX(ReceivedAt) FROM BookActivityEvents)
                """;

            using var reader = cmd.ExecuteReader();
            reader.Read();

            var fingerprint = new StringBuilder();
            for (var i = 0; i < reader.FieldCount; i++)
                fingerprint.Append(reader.IsDBNull(i) ? "-" : reader.GetValue(i).ToString()).Append('|');

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()));
            return Convert.ToHexStringLower(hash)[..16];
        });
    }
}
