using Alexandria.Business.Models.Settings;

namespace Alexandria.Business.Interfaces;

public interface IAlexandriaSettingsService
{
    Task<AppSettings> Get();
    Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp);
    Task UpdateSettings(AppSettings settings);
}
