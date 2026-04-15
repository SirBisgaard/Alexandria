using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.Business.Library.Interfaces;

public interface ISettingsService
{
    Task<AppSettings> Get();
    Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp);
    Task UpdateSettings(AppSettings settings);
}
