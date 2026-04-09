using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.Business.Library.Services;

public class AlexandriaSettingsService(ISettingsRepository settingsRepository) : IAlexandriaSettingsService
{
    private static AppSettings? cached;

    public async Task<AppSettings> Get()
    {
        if (cached is not null)
            return cached;

        cached = await settingsRepository.Get();
        return cached;
    }

    public async Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp)
    {
        await settingsRepository.UpdateLastIndexedTimestamp(timestamp);
        cached = null;
    }

    public async Task UpdateSettings(AppSettings settings)
    {
        await settingsRepository.UpdateSettings(settings);
        cached = null;
    }
}
