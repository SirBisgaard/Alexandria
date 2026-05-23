using Alexandria.Business.Integrations.Models;

namespace Alexandria.Business.Interfaces;

public interface IAudioBookShelfSyncIntegrationRepository
{
    Task Upsert(AudioBookShelfIntegration integration);
    Task<AudioBookShelfIntegration?> GetByUserUuid(Guid userUuid);
    Task<List<AudioBookShelfIntegration>> GetAllEnabled();
    Task UpdateLastSynced(Guid userUuid, DateTimeOffset timestamp);

    Task<Dictionary<Guid, DateTimeOffset>> GetBookSyncedStates(Guid userUuid);
    Task UpsertBookSyncedState(Guid userUuid, Guid bookUuid, DateTimeOffset absLastUpdate);
    Task DeleteBookSyncedState(Guid userUuid, Guid bookUuid);
}
