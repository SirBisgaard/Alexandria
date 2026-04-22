using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class SettingsRepositoryTests : DatabaseTestBase
{
    private SettingsRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new SettingsRepository(ConnectionProvider);
    }

    [TestMethod]
    public async Task SettingsRepository_Get_ReturnsSeededDefaults()
    {
        var settings = await repository.Get();

        Assert.AreNotEqual(Guid.Empty, settings.UuidSeed);
        Assert.IsNull(settings.LastIndexedTimestamp);
        Assert.AreEqual(60, settings.SyncBatchSize);
        Assert.IsFalse(settings.ProxyLoggingEnabled);
        Assert.AreEqual(250, settings.WordsPerPage);
        Assert.IsFalse(settings.SyncLibraryAtBoot);
        Assert.AreEqual(M4BFileTreatment.Nothing, settings.M4BFileTreatment);
    }

    [TestMethod]
    public async Task SettingsRepository_UpdateSettings_RoundTripsMutableFields()
    {
        var updated = new AppSettings
        {
            SyncBatchSize = 120,
            ProxyLoggingEnabled = false,
            WordsPerPage = 300,
            SyncLibraryAtBoot = true,
            M4BFileTreatment = M4BFileTreatment.TranscodeAtIndex
        };

        await repository.UpdateSettings(updated);

        var fetched = await repository.Get();
        Assert.AreEqual(120, fetched.SyncBatchSize);
        Assert.IsFalse(fetched.ProxyLoggingEnabled);
        Assert.AreEqual(300, fetched.WordsPerPage);
        Assert.IsTrue(fetched.SyncLibraryAtBoot);
        Assert.AreEqual(M4BFileTreatment.TranscodeAtIndex, fetched.M4BFileTreatment);
    }

    [TestMethod]
    public async Task SettingsRepository_UpdateSettings_PreservesUuidSeed()
    {
        var originalSeed = (await repository.Get()).UuidSeed;

        await repository.UpdateSettings(new AppSettings
        {
            SyncBatchSize = 10,
            ProxyLoggingEnabled = false,
            WordsPerPage = 100,
            SyncLibraryAtBoot = false,
            M4BFileTreatment = M4BFileTreatment.Nothing
        });

        var fetchedSeed = (await repository.Get()).UuidSeed;
        Assert.AreEqual(originalSeed, fetchedSeed);
    }

    [TestMethod]
    public async Task SettingsRepository_Get_FileRenameTemplateDefaultsToSeededValue()
    {
        var settings = await repository.Get();

        Assert.AreEqual(FileNameTemplate.DefaultTemplate, settings.FileRenameTemplate);
    }

    [TestMethod]
    public async Task SettingsRepository_UpdateSettings_FileRenameTemplateRoundTrips()
    {
        await repository.UpdateSettings(new AppSettings
        {
            SyncBatchSize = 60,
            WordsPerPage = 250,
            M4BFileTreatment = M4BFileTreatment.Nothing,
            FileRenameTemplate = "<Authors> - <Title>"
        });

        var fetched = await repository.Get();
        Assert.AreEqual("<Authors> - <Title>", fetched.FileRenameTemplate);
    }

    [TestMethod]
    public async Task SettingsRepository_UpdateLastIndexedTimestamp_Persists()
    {
        var timestamp = new DateTimeOffset(2026, 4, 17, 12, 0, 0, TimeSpan.Zero);

        await repository.UpdateLastIndexedTimestamp(timestamp);

        var fetched = await repository.Get();
        Assert.IsNotNull(fetched.LastIndexedTimestamp);
        Assert.AreEqual(timestamp, fetched.LastIndexedTimestamp.Value);
    }
}
