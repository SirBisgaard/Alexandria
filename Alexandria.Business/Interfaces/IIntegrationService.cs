using Alexandria.Business.Integrations.Models;

namespace Alexandria.Business.Interfaces;

public interface IIntegrationService
{
    Task UpsertAudioBookShelf(AudioBookShelfIntegration integration);
    Task<AudioBookShelfIntegration?> GetAudioBookShelf(Guid userUuid);
    Task<bool> VerifyAudioBookShelfConnection(string serverUrl, string apiToken);
}
