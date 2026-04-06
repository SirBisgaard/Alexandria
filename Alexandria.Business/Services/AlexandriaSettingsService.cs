using Alexandria.Business.Interfaces;
using Alexandria.Business.Models.Settings;

namespace Alexandria.Business.Services;

public class AlexandriaSettingsService(ISettingsRepository settingsRepository) : IAlexandriaSettingsService
{
    public async Task<AppSettings> Get() => await settingsRepository.Get();

    public async Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp) => await settingsRepository.UpdateLastIndexedTimestamp(timestamp);

    public async Task UpdateSettings(int syncBatchSize, bool proxyLoggingEnabled) => await settingsRepository.UpdateSettings(syncBatchSize, proxyLoggingEnabled);
}
