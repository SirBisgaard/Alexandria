using Alexandria.Business.Models.Settings;

namespace Alexandria.Business.Interfaces;

public interface ISettingsRepository
{
    Task<AppSettings> Get();
    Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp);
    Task UpdateSyncBatchSize(int batchSize);
}
