using Alexandria.Business.Integrations.Models;
using Alexandria.Business.Interfaces;

namespace Alexandria.Business.Integrations;

public class IntegrationService(IAudioBookShelfSyncIntegration audioBookShelf, IAudioBookShelfSyncIntegrationRepository audioBookShelfRepository) : IIntegrationService
{
    public Task UpsertAudioBookShelf(AudioBookShelfIntegration integration)
    {
        return audioBookShelfRepository.Upsert(integration);
    }

    public Task<AudioBookShelfIntegration?> GetAudioBookShelf(Guid userUuid)
    {
        return audioBookShelfRepository.GetByUserUuid(userUuid);
    }

    public Task<bool> VerifyAudioBookShelfConnection(string serverUrl, string apiToken)
    {
        return audioBookShelf.VerifyConnection(serverUrl, apiToken);
    }
}
