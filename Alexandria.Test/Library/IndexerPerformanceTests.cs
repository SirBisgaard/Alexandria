using System.Collections.Concurrent;
using System.Diagnostics;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Alexandria.DataAccess.Database;
using Alexandria.DataAccess.Database.Repositories;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files;
using Alexandria.DataAccess.Files.Books.Audiobooks;
using Alexandria.DataAccess.Files.Books.Ebooks;
using Alexandria.DataAccess.Files.Interfaces;
using Alexandria.DataAccess.Files.Services;
using Alexandria.Business.Library.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Library;

[TestClass]
[Ignore]
public sealed class IndexerPerformanceTests
{
    private const string EbookFolder = "/path/to/books/Ebooks";
    private const string AudiobookFolder = "/path/to/books/Audiobooks";

    private string testDir = null!;
    private ConcurrentBag<TimingRecord> timings = null!;

    [TestInitialize]
    public void Setup()
    {
        testDir = Path.Combine(Path.GetTempPath(), "alexandria-perf-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        timings = new ConcurrentBag<TimingRecord>();
        GuidExtensions.SetNamespace(Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testDir))
            Directory.Delete(testDir, recursive: true);
    }

    private static void RequireProductionLibrary()
    {
        if (!Directory.Exists(EbookFolder) && !Directory.Exists(AudiobookFolder))
            Assert.Inconclusive("Production library not available — skipping.");
    }

    private AlexandriaSettings CreateSettings() => new() { PublicUrl = "http://localhost", DataFolder = testDir, EbookFolder = EbookFolder, AudiobookFolder = AudiobookFolder };

    [TestMethod]
    [Timeout(600_000)]
    public async Task FullIndex_ProductionLibrary_TimingReport()
    {
        RequireProductionLibrary();

        var settings = CreateSettings();
        var connectionProvider = new SqliteConnectionProvider(settings);
        var initializer = new DatabaseInitializer(connectionProvider, NullLogger<DatabaseInitializer>.Instance);
        await initializer.Initialize();

        // Raw repositories (no caching layer)
        var authorRepository = new AuthorRepository(connectionProvider);
        var seriesRepository = new SeriesRepository(connectionProvider);
        var bookRepository = new BookRepository(connectionProvider, authorRepository, seriesRepository);

        // Instrumented wrappers around raw repos
        var instrumentedBookRepo = new InstrumentedBookRepository(bookRepository, timings);
        var instrumentedAuthorRepo = new InstrumentedAuthorRepository(authorRepository, timings);
        var instrumentedSeriesRepo = new InstrumentedSeriesRepository(seriesRepository, timings);

        // Timing ffmpeg wrapper
        var timingFfmpeg = new TimingFfmpegHandler(new FfmpegHandler(), timings);
        var m4bHandler = new M4BMetadataHandler(timingFfmpeg);
        var audiobookMediaHandler = new FileAudiobookMediaHandler(m4bHandler);

        // File indexers with timing wrappers
        var coverThumbnailService = new CoverThumbnailService(settings, NullLogger<CoverThumbnailService>.Instance);
        var fileRepository = new FileRepository(settings, coverThumbnailService);
        var epubIndexer = new TimingFileContentIndexer(new FileEbookIndexer(settings, fileRepository, new EpubMetadataHandler(), new KepubifyConverter(), coverThumbnailService), timings);
        var audiobookIndexer = new TimingFileContentIndexer(new FileAudiobookIndexer(settings, audiobookMediaHandler, coverThumbnailService), timings);

        var epubMediaHandler = new FileEbbokMediaHandler(new EpubMetadataHandler(), fileRepository);
        var contentManager = new FileContentManager(new IFileContentIndexer[] { epubIndexer, audiobookIndexer }, new IFileContentMediaHandler[] { epubMediaHandler, audiobookMediaHandler }, NullLogger<FileContentManager>.Instance);
        var settingsService = new SettingsService(new SettingsRepository(connectionProvider), new KoboTrafficLogRepository(connectionProvider), NullLogger<SettingsService>.Instance);
        var indexer = new IndexerService(contentManager, instrumentedBookRepo, settingsService, instrumentedAuthorRepo, instrumentedSeriesRepo, connectionProvider, NullLogger<IndexerService>.Instance);

        // Run full index with timing
        var totalSw = Stopwatch.StartNew();
        var count = await indexer.Index();
        totalSw.Stop();

        // Generate and output report
        var report = PerformanceReport.Generate(timings, EbookFolder, AudiobookFolder, totalSw.ElapsedMilliseconds, instrumentedBookRepo, instrumentedAuthorRepo, instrumentedSeriesRepo);
        Console.WriteLine(report);

        // Basic assertions
        Assert.IsGreaterThan(0, count, "Should index at least one file.");

        var books = await bookRepository.GetAll();
        foreach (var book in books)
        {
            Assert.AreNotEqual(string.Empty, book.Title);
        }

        var m4bBooks = books.Where(b => b.MediaType == MediaType.Audiobook).ToList();
        foreach (var book in m4bBooks)
        {
            Assert.IsGreaterThan(0, book.Duration!.Value, $"Audiobook '{book.Title}' should have duration.");
            Assert.IsNotEmpty(book.Chapters, $"Audiobook '{book.Title}' should have chapters.");
        }
    }

    [TestMethod]
    [Timeout(600_000)]
    public async Task FfmpegPerformance_AllM4B_MeasuresPerFile()
    {
        if (!Directory.Exists(AudiobookFolder))
            Assert.Inconclusive("Audiobook folder not available — skipping.");

        var m4bFiles = Directory.GetFiles(AudiobookFolder, "*.m4b");
        if (m4bFiles.Length == 0)
            Assert.Inconclusive("No M4B files found — skipping.");

        var ffmpegHandler = new FfmpegHandler();

        // Sample: 10 largest + 5 smallest
        var sorted = m4bFiles.Select(f => new FileInfo(f)).OrderByDescending(f => f.Length).ToList();
        var sample = sorted.Take(10).Concat(sorted.TakeLast(5)).DistinctBy(f => f.FullName).ToList();

        Console.WriteLine("=== FFPROBE + FFMPEG COVER PERFORMANCE (M4B FILES — SAMPLED) ===");
        Console.WriteLine($"Total M4B files: {m4bFiles.Length}, sampling {sample.Count}");
        Console.WriteLine();

        Console.WriteLine("--- FFPROBE (metadata + chapters) ---");
        Console.WriteLine($"{"File",-60} {"Size",10} {"Time",10}");
        long probeTotal = 0;
        foreach (var fileInfo in sample)
        {
            var sw = Stopwatch.StartNew();
            var json = await ffmpegHandler.GetMetadataJson(fileInfo.FullName);
            sw.Stop();
            probeTotal += sw.ElapsedMilliseconds;

            var name = fileInfo.Name;
            if (name.Length > 58)
                name = name[..55] + "...";

            Console.WriteLine($"{name,-60} {FormatBytes(fileInfo.Length),10} {FormatMs(sw.ElapsedMilliseconds),10}");
            Assert.IsFalse(string.IsNullOrEmpty(json));
        }
        Console.WriteLine($"{"TOTAL",-60} {"",10} {FormatMs(probeTotal),10}");

        Console.WriteLine();
        Console.WriteLine("--- FFMPEG COVER EXTRACTION ---");
        Console.WriteLine($"{"File",-60} {"Size",10} {"Time",10} {"Cover",8}");
        long coverTotal = 0;
        foreach (var fileInfo in sample)
        {
            var sw = Stopwatch.StartNew();
            var coverData = await ffmpegHandler.ExtractCoverImage(fileInfo.FullName);
            sw.Stop();
            coverTotal += sw.ElapsedMilliseconds;

            var name = fileInfo.Name;
            if (name.Length > 58)
                name = name[..55] + "...";

            var coverSize = coverData is not null ? FormatBytes(coverData.Length) : "none";
            Console.WriteLine($"{name,-60} {FormatBytes(fileInfo.Length),10} {FormatMs(sw.ElapsedMilliseconds),10} {coverSize,8}");
        }
        Console.WriteLine($"{"TOTAL",-60} {"",10} {FormatMs(coverTotal),10}");
    }

    private static string FormatMs(long ms)
    {
        if (ms < 1000)
            return $"{ms}ms";
        if (ms < 60_000)
            return $"{ms / 1000.0:F1}s";
        return $"{ms / 60_000}m {ms % 60_000 / 1000}s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F0} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
