using System.Reflection;
using Alexandria.Business.Interfaces;
using Alexandria.Business.KoboStoreProxy.Interfaces;
using Alexandria.Business.KoboStoreProxy.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.BusinessLayer;

/// <summary>
/// SettingsService caches the loaded AppSettings in a <see langword="private static"/>
/// field, so tests must reset that field and not run in parallel.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsServiceTests
{
    private FakeSettingsRepository settingsRepository = null!;
    private FakeKoboTrafficLogRepository trafficLogRepository = null!;
    private SettingsService service = null!;

    [TestInitialize]
    public void Setup()
    {
        ResetCachedStatic();
        settingsRepository = new FakeSettingsRepository();
        trafficLogRepository = new FakeKoboTrafficLogRepository();
        service = new SettingsService(settingsRepository, trafficLogRepository, NullLogger<SettingsService>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        ResetCachedStatic();
    }

    [TestMethod]
    public async Task Get_FirstCall_LoadsFromRepository()
    {
        settingsRepository.Seed(new AppSettings { WordsPerPage = 321, ProxyLoggingEnabled = false });

        var result = await service.Get();

        Assert.AreEqual(321, result.WordsPerPage);
        Assert.IsFalse(result.ProxyLoggingEnabled);
        Assert.AreEqual(1, settingsRepository.GetCallCount);
    }

    [TestMethod]
    public async Task Get_SubsequentCalls_ReturnsCachedWithoutRepoHit()
    {
        settingsRepository.Seed(new AppSettings { WordsPerPage = 111 });

        await service.Get();
        await service.Get();
        await service.Get();

        Assert.AreEqual(1, settingsRepository.GetCallCount);
    }

    [TestMethod]
    public async Task UpdateSettings_InvalidatesCache_NextGetReloads()
    {
        settingsRepository.Seed(new AppSettings { WordsPerPage = 111 });
        await service.Get();
        Assert.AreEqual(1, settingsRepository.GetCallCount);

        await service.UpdateSettings(new AppSettings { WordsPerPage = 222 });

        var refreshed = await service.Get();
        Assert.AreEqual(222, refreshed.WordsPerPage);
        Assert.IsGreaterThan(1, settingsRepository.GetCallCount);
    }

    [TestMethod]
    public async Task UpdateSettings_ProxyLoggingDisabled_ClearsTrafficLogs()
    {
        settingsRepository.Seed(new AppSettings { ProxyLoggingEnabled = true });

        await service.UpdateSettings(new AppSettings { ProxyLoggingEnabled = false });

        Assert.AreEqual(1, trafficLogRepository.ClearCount);
    }

    [TestMethod]
    public async Task UpdateSettings_ProxyLoggingStillEnabled_DoesNotClearLogs()
    {
        settingsRepository.Seed(new AppSettings { ProxyLoggingEnabled = true });

        await service.UpdateSettings(new AppSettings { ProxyLoggingEnabled = true });

        Assert.AreEqual(0, trafficLogRepository.ClearCount);
    }

    [TestMethod]
    public async Task UpdateSettings_ProxyLoggingToggledFromDisabledToEnabled_DoesNotClearLogs()
    {
        settingsRepository.Seed(new AppSettings { ProxyLoggingEnabled = false });

        await service.UpdateSettings(new AppSettings { ProxyLoggingEnabled = true });

        Assert.AreEqual(0, trafficLogRepository.ClearCount);
    }

    [TestMethod]
    public async Task UpdateSettings_M4BFileTreatmentRoundTripsThroughRepository()
    {
        settingsRepository.Seed(new AppSettings { M4BFileTreatment = M4BFileTreatment.Nothing });

        await service.UpdateSettings(new AppSettings { M4BFileTreatment = M4BFileTreatment.ConvertAtIndex });

        var refreshed = await service.Get();
        Assert.AreEqual(M4BFileTreatment.ConvertAtIndex, refreshed.M4BFileTreatment);
    }

    [TestMethod]
    public async Task UpdateSettings_WordsPerPageRoundTripsThroughRepository()
    {
        settingsRepository.Seed(new AppSettings { WordsPerPage = 250 });

        await service.UpdateSettings(new AppSettings { WordsPerPage = 400 });

        var refreshed = await service.Get();
        Assert.AreEqual(400, refreshed.WordsPerPage);
    }

    [TestMethod]
    public async Task UpdateSettings_SyncLibraryAtBootRoundTripsThroughRepository()
    {
        settingsRepository.Seed(new AppSettings { SyncLibraryAtBoot = false });

        await service.UpdateSettings(new AppSettings { SyncLibraryAtBoot = true });

        var refreshed = await service.Get();
        Assert.IsTrue(refreshed.SyncLibraryAtBoot);
    }

    [TestMethod]
    public async Task UpdateLastIndexedTimestamp_PersistsAndInvalidatesCache()
    {
        var initialTimestamp = DateTimeOffset.UtcNow.AddDays(-1);
        settingsRepository.Seed(new AppSettings { LastIndexedTimestamp = initialTimestamp });
        await service.Get();

        var newTimestamp = DateTimeOffset.UtcNow;
        await service.UpdateLastIndexedTimestamp(newTimestamp);

        Assert.AreEqual(newTimestamp, settingsRepository.Stored.LastIndexedTimestamp);
        var refreshed = await service.Get();
        Assert.AreEqual(newTimestamp, refreshed.LastIndexedTimestamp);
    }

    private static void ResetCachedStatic()
    {
        var field = typeof(SettingsService).GetField("cached", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, null);
    }

    private sealed class FakeSettingsRepository : ISettingsRepository
    {
        public AppSettings Stored { get; private set; } = new();
        public int GetCallCount { get; private set; }

        public void Seed(AppSettings settings) => Stored = settings;

        public Task<AppSettings> Get()
        {
            GetCallCount++;
            return Task.FromResult(Clone(Stored));
        }

        public Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp)
        {
            Stored.LastIndexedTimestamp = timestamp;
            return Task.CompletedTask;
        }

        public Task UpdateSettings(AppSettings settings)
        {
            Stored = Clone(settings);
            return Task.CompletedTask;
        }

        private static AppSettings Clone(AppSettings source)
        {
            return new AppSettings
            {
                UuidSeed = source.UuidSeed,
                LastIndexedTimestamp = source.LastIndexedTimestamp,
                SyncBatchSize = source.SyncBatchSize,
                ProxyLoggingEnabled = source.ProxyLoggingEnabled,
                WordsPerPage = source.WordsPerPage,
                SyncLibraryAtBoot = source.SyncLibraryAtBoot,
                M4BFileTreatment = source.M4BFileTreatment
            };
        }
    }

    private sealed class FakeKoboTrafficLogRepository : IKoboTrafficLogRepository
    {
        public int ClearCount { get; private set; }

        public Task Log(KoboProxyTrafficLog entry) => Task.CompletedTask;

        public Task Clear()
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }
}
