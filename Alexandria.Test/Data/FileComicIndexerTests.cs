using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Books.Comics;
using Alexandria.DataAccess.Files.Interfaces;
using Alexandria.DataAccess.Files.Services;
using Alexandria.Tests.TestData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class FileComicIndexerTests
{
    private static readonly ComicDeviceProfile LibraColour = ComicDeviceProfile.ForModelCode("0390")!;
    private static readonly ComicDeviceProfile ClaraBw = ComicDeviceProfile.ForModelCode("0384")!;

    private string tempDir = null!;
    private AlexandriaSettings settings = null!;
    private FakeKoboDeviceRepository devices = null!;
    private FakeSettingsService settingsService = null!;
    private ComicRenditionStore store = null!;
    private CountingBuilder builder = null!;
    private FileComicIndexer indexer = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        settings = new AlexandriaSettings { PublicUrl = "http://localhost", DataFolder = Path.Combine(tempDir, "data"), EbookFolder = Path.Combine(tempDir, "ebooks"), AudiobookFolder = Path.Combine(tempDir, "audiobooks") };
        Directory.CreateDirectory(settings.EbookFolder);
        devices = new FakeKoboDeviceRepository();
        settingsService = new FakeSettingsService();
        store = new ComicRenditionStore(settings, settingsService, devices);
        builder = new CountingBuilder(new ComicEpubBuilder(NullLogger<ComicEpubBuilder>.Instance));
        var thumbnails = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
        indexer = new FileComicIndexer(settings, new CbzFileHandler(), builder, store, new FileRepository(settings, thumbnails), thumbnails, settingsService, new CbrComicConverterTests.NullProgressService(), NullLogger<FileComicIndexer>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public void ComicDeviceProfile_KnownModels_MapToScreenProfiles()
    {
        Assert.AreEqual("1264x1680-color", LibraColour.Key);
        Assert.AreEqual("1072x1448-gray", ClaraBw.Key);
        Assert.AreEqual("1264x1680-gray", ComicDeviceProfile.ForModelCode("0376")!.Key);
        Assert.IsNull(ComicDeviceProfile.ForModelCode("9999"));
        Assert.IsNull(ComicDeviceProfile.ForModelCode(null));
    }

    [TestMethod]
    public async Task ComicRenditionStore_ActiveProfiles_AreDistinctAndSkipUnknownModels()
    {
        devices.Add("0387", "0390", "0384", "9999", null);

        var profiles = await store.GetActiveProfiles();

        CollectionAssert.AreEquivalent(new[] { LibraColour, ClaraBw }, profiles);
    }

    [TestMethod]
    public async Task ComicRenditionStore_Path_ChangesWithColourModeDirectionAndSourceFile()
    {
        var book = IndexedBook("Ann - Saga.cbz");
        var original = await store.GetRenditionPath(book, LibraColour);

        book.File.ColorMode = ComicColorMode.Grayscale;
        var afterColourMode = await store.GetRenditionPath(book, LibraColour);
        book.File.ReadingDirection = ComicReadingDirection.RightToLeft;
        var afterDirection = await store.GetRenditionPath(book, LibraColour);
        File.SetLastWriteTimeUtc(book.File.SourcePath, DateTime.UtcNow.AddMinutes(5));
        var afterSourceChange = await store.GetRenditionPath(book, LibraColour);

        Assert.AreEqual(4, new[] { original, afterColourMode, afterDirection, afterSourceChange }.Distinct().Count());
        Assert.StartsWith(Path.Combine(settings.ComicsFolder, "1264x1680-color", book.BookUuid.ToString()), original);
    }

    [TestMethod]
    public async Task ComicRenditionStore_Path_ChangesWhenAnyRenderingSettingChanges()
    {
        var book = IndexedBook("Ann - Saga.cbz");
        var original = await store.GetRenditionPath(book, LibraColour);

        settingsService.Settings.ComicRendering = settingsService.Settings.ComicRendering with { PreserveMarginPercent = 2 };
        var tuned = await store.GetRenditionPath(book, LibraColour);
        settingsService.Settings.ComicRendering = new ComicRenderingSettings();
        var restored = await store.GetRenditionPath(book, LibraColour);

        Assert.AreNotEqual(original, tuned);
        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public async Task FileComicIndexer_CreateDerivativeFiles_RebuildsWhenRenderingSettingsChange()
    {
        devices.Add("0390");
        var book = IndexedBook("Ann - Saga.cbz");
        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);
        settingsService.Settings.ComicRendering = settingsService.Settings.ComicRendering with { JpegQuality = 60 };

        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);

        Assert.AreEqual(2, builder.Builds);
        Assert.AreEqual(60, builder.LastRendering?.JpegQuality, "the builder must receive the current settings");
        Assert.HasCount(1, Directory.GetFiles(Path.Combine(settings.ComicsFolder, LibraColour.Key)));
    }

    [TestMethod]
    public async Task FileComicIndexer_CreateDerivativeFiles_BuildsOneRenditionPerActiveProfile()
    {
        devices.Add("0390", "0387", "0384");
        var book = IndexedBook("Ann - Saga.cbz");

        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);

        Assert.AreEqual(2, builder.Builds);
        Assert.IsNotNull(await store.FindRendition(book, LibraColour));
        Assert.IsNotNull(await store.FindRendition(book, ClaraBw));
        Assert.AreEqual(new FileInfo(book.File.SourcePath).Length, book.File.TotalFileSize);
    }

    [TestMethod]
    public async Task FileComicIndexer_CreateDerivativeFiles_SkipsExistingAndReplacesStaleVariants()
    {
        devices.Add("0390");
        var book = IndexedBook("Ann - Saga.cbz");
        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);
        var firstRendition = (await store.FindRendition(book, LibraColour))!.FullName;

        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);
        Assert.AreEqual(1, builder.Builds, "an existing rendition must not be rebuilt");

        book.File.ColorMode = ComicColorMode.Grayscale;
        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);

        Assert.AreEqual(2, builder.Builds);
        Assert.IsFalse(File.Exists(firstRendition), "the previous colour-mode rendition must be removed");
        Assert.HasCount(1, Directory.GetFiles(Path.Combine(settings.ComicsFolder, LibraColour.Key)));
    }

    [TestMethod]
    public async Task FileComicIndexer_CreateDerivativeFiles_NoKnownDevicesBuildsNothing()
    {
        devices.Add((string?)null);
        var book = IndexedBook("Ann - Saga.cbz");

        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);

        Assert.AreEqual(0, builder.Builds);
    }

    [TestMethod]
    public async Task FileComicIndexer_CreateDerivativeFiles_BrokenArchiveIsLoggedNotThrown()
    {
        devices.Add("0390");
        var book = IndexedBook("Ann - Saga.cbz");
        await File.WriteAllBytesAsync(book.File.SourcePath, "corrupted"u8.ToArray());

        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);

        Assert.IsNull(await store.FindRendition(book, LibraColour));
    }

    [TestMethod]
    public async Task FileComicIndexer_CleanupOrphanedFiles_RemovesUnknownBooksAndUnusedProfiles()
    {
        devices.Add("0390", "0384");
        var kept = IndexedBook("Ann - Kept.cbz");
        var removed = IndexedBook("Ann - Removed.cbz");
        await indexer.CreateDerivativeFiles(kept, kept.File.SourcePath);
        await indexer.CreateDerivativeFiles(removed, removed.File.SourcePath);
        devices.Clear();
        devices.Add("0390");

        indexer.CleanupOrphanedFiles([kept.BookUuid], []);

        Assert.IsNotNull(await store.FindRendition(kept, LibraColour));
        Assert.IsNull(await store.FindRendition(removed, LibraColour));
        Assert.IsFalse(Directory.Exists(Path.Combine(settings.ComicsFolder, ClaraBw.Key)), "profile folders no device uses must be removed");
    }

    [TestMethod]
    public async Task FileComicIndexer_DeleteDerivedFiles_RemovesRenditionsInEveryProfile()
    {
        devices.Add("0390", "0384");
        var book = IndexedBook("Ann - Saga.cbz");
        await indexer.CreateDerivativeFiles(book, book.File.SourcePath);

        indexer.DeleteDerivedFiles(book);

        Assert.IsNull(await store.FindRendition(book, LibraColour));
        Assert.IsNull(await store.FindRendition(book, ClaraBw));
    }

    private Book IndexedBook(string fileName)
    {
        var path = ComicFixtures.CreateSimpleCbz(settings.EbookFolder, fileName);
        var book = new CbzFileHandler().ExtractMetadata(path);
        book.BookUuid = Guid.NewGuid();
        book.File.SourcePath = path;
        return book;
    }

    private sealed class CountingBuilder(IComicEpubBuilder inner) : IComicEpubBuilder
    {
        public int Builds;
        public ComicRenderingSettings? LastRendering;

        public void Build(Book book, ComicDeviceProfile profile, ComicRenderingSettings rendering, string outputPath, Action<int, int>? reportProgress = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Builds);
            LastRendering = rendering;
            inner.Build(book, profile, rendering, outputPath, reportProgress, cancellationToken);
        }
    }

    internal sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; } = new();

        public Task<AppSettings> Get() => Task.FromResult(Settings);
        public Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp) => Task.CompletedTask;
        public Task UpdateSettings(AppSettings settings) => Task.CompletedTask;
    }

    internal sealed class FakeKoboDeviceRepository : IKoboDeviceRepository
    {
        private readonly List<KoboDevice> devices = [];

        public void Add(params string?[] modelCodes)
        {
            foreach (var code in modelCodes)
                devices.Add(new KoboDevice { DeviceUuid = Guid.NewGuid(), Nickname = "Device", ModelCode = code });
        }

        public void Clear() => devices.Clear();

        public Task Create(KoboDevice device) { devices.Add(device); return Task.CompletedTask; }
        public Task<KoboDevice?> GetByUuid(Guid deviceUuid) => Task.FromResult(devices.FirstOrDefault(d => d.DeviceUuid == deviceUuid));
        public Task<List<KoboDevice>> GetAll() => Task.FromResult(devices.ToList());
        public Task Update(KoboDevice device) => Task.CompletedTask;
        public Task Delete(Guid deviceUuid) { devices.RemoveAll(d => d.DeviceUuid == deviceUuid); return Task.CompletedTask; }
        public Task UpdateLastSynced(Guid deviceUuid) => Task.CompletedTask;
        public Task SetLocked(Guid deviceUuid, bool locked) => Task.CompletedTask;
        public Task BindDevice(Guid deviceUuid, string serialNumber, string? modelName, string? modelCode, string? firmware) => Task.CompletedTask;
    }
}
