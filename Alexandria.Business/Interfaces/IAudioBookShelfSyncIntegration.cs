namespace Alexandria.Business.Interfaces;

public interface IAudioBookShelfSyncIntegration
{
    Task<bool> VerifyConnection(string serverUrl, string apiToken);
    Task Sync(CancellationToken cancellationToken);
    Task SyncForUser(Guid userUuid, CancellationToken cancellationToken);
}