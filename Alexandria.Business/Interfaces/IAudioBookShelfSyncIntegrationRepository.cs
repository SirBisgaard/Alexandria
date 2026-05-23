using Alexandria.Business.Integrations.Models;

namespace Alexandria.Business.Interfaces;

public interface IAudioBookShelfSyncIntegrationRepository
{
    Task Upsert(AudioBookShelfIntegration integration);
    Task<AudioBookShelfIntegration?> GetByUserUuid(Guid userUuid);
    Task<List<AudioBookShelfIntegration>> GetAllEnabled();
    Task UpdateLastSynced(Guid userUuid, DateTimeOffset timestamp);
}
