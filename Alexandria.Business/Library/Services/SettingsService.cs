using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Settings;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Library.Services;

public class SettingsService(ISettingsRepository settingsRepository, IKoboTrafficLogRepository koboTrafficLogRepository, ILogger<SettingsService> logger) : ISettingsService
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
        logger.LogInformation("Updating application settings");

        var validation = FileNameTemplate.Validate(settings.FileRenameTemplate);
        if (!validation.IsValid)
            throw new ArgumentException($"FileRenameTemplate is invalid: {validation.Error}", nameof(settings));

        var current = await Get();
        await settingsRepository.UpdateSettings(settings);
        cached = null;

        if (current.ProxyLoggingEnabled && !settings.ProxyLoggingEnabled)
        {
            logger.LogInformation("Proxy logging disabled, clearing traffic logs");
            await koboTrafficLogRepository.Clear();
        }
    }
}
