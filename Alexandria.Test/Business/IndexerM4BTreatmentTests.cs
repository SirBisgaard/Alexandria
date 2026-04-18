using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.BusinessLayer;

/// <summary>
/// Verifies <see cref="IndexerService.Index"/> orchestration under each <see cref="M4BFileTreatment"/>.
/// The concrete treatment-mode behavior (ffmpeg chapter transcoding, M4B deletion, MP3 folder creation)
/// lives inside the file-layer implementations and is covered by Data integration tests. These tests
/// lock down the business-layer seam: converters run before discovery, and converter-produced side
/// effects (e.g. replacing an M4B with an MP3 folder) are reflected in the discovery pass.
/// </summary>
[TestClass]
public sealed class IndexerM4BTreatmentTests
{
    private FakeFileContentManager contentManager = null!;
    private FakeBookRepository bookRepository = null!;
    private FakeSettingsService settingsService = null!;
    private FakeAuthorRepository authorRepository = null!;
    private FakeSeriesRepository seriesRepository = null!;
    private FakeDatabasePersistence databasePersistence = null!;
    private TreatmentAwareFileConverter converter = null!;
    private FakeIndexingProgressService progressService = null!;
    private IndexerService service = null!;
    private List<string> callLog = null!;

    [TestInitialize]
    public void Setup()
    {
        GuidExtensions.SetNamespace(Guid.NewGuid());
        callLog = [];
        contentManager = new FakeFileContentManager(callLog);
        bookRepository = new FakeBookRepository();
        settingsService = new FakeSettingsService();
        authorRepository = new FakeAuthorRepository();
        seriesRepository = new FakeSeriesRepository();
        databasePersistence = new FakeDatabasePersistence();
        converter = new TreatmentAwareFileConverter(callLog, settingsService, contentManager);
        progressService = new FakeIndexingProgressService();
        service = new IndexerService(
            contentManager,
            bookRepository,
            settingsService,
            authorRepository,
            seriesRepository,
            databasePersistence,
            [converter],
            progressService,
            NullLogger<IndexerService>.Instance);
    }

    [TestMethod]
    public async Task Treatment_Nothing_ConverterReadsSettingAndSkipsConversion()
    {
        settingsService.Settings.M4BFileTreatment = M4BFileTreatment.Nothing;
        var m4b = MakeAudiobook("Listened", "/audiobooks/listened.m4b");
        contentManager.SeedBooksToDiscover((m4b, null));

        await service.Index();

        Assert.AreEqual(M4BFileTreatment.Nothing, converter.LastObservedTreatment);
        Assert.AreEqual(0, converter.ConvertedBookCount);
        // Discovery unaltered — M4B persists as an M4B
        Assert.AreEqual(MediaType.Audiobook, bookRepository.All.Single().File.MediaType);
        Assert.IsTrue(bookRepository.All.Single().File.SourcePath.EndsWith(".m4b"));
    }

    [TestMethod]
    public async Task Treatment_TranscodeAtIndex_ConverterReadsSettingButDoesNotAlterSource()
    {
        settingsService.Settings.M4BFileTreatment = M4BFileTreatment.TranscodeAtIndex;
        var m4b = MakeAudiobook("Chapters", "/audiobooks/ch.m4b");
        contentManager.SeedBooksToDiscover((m4b, null));

        await service.Index();

        Assert.AreEqual(M4BFileTreatment.TranscodeAtIndex, converter.LastObservedTreatment);
        Assert.AreEqual(0, converter.ConvertedBookCount);
        // TranscodeAtIndex is internal to the M4B indexer's CreateDerivativeFiles — source still M4B at the business level
        Assert.IsTrue(bookRepository.All.Single().File.SourcePath.EndsWith(".m4b"));
    }

    [TestMethod]
    public async Task Treatment_ConvertAtIndex_ConverterMutatesDiscoveryAndBookIsIndexedAsMp3Folder()
    {
        settingsService.Settings.M4BFileTreatment = M4BFileTreatment.ConvertAtIndex;
        // The converter will see no M4Bs yet, but it pretends to replace the M4B with an MP3 folder.
        // We stage the pre-converted M4B — the converter substitutes its own replacement into discovery.
        var m4b = MakeAudiobook("ToConvert", "/audiobooks/src.m4b");
        converter.SimulatedReplacement = (m4b, MakeMp3FolderAudiobook("ToConvert", "/audiobooks/ToConvert-folder"));

        await service.Index();

        Assert.AreEqual(M4BFileTreatment.ConvertAtIndex, converter.LastObservedTreatment);
        Assert.AreEqual(1, converter.ConvertedBookCount);
        var stored = bookRepository.All.Single();
        Assert.AreEqual("ToConvert", stored.Title);
        Assert.AreEqual(MediaType.Audiobook, stored.File.MediaType);
        Assert.EndsWith("ToConvert-folder", stored.File.SourcePath);
    }

    [TestMethod]
    public async Task AnyTreatment_ConverterRunsBeforeFileDiscovery()
    {
        foreach (var treatment in Enum.GetValues<M4BFileTreatment>())
        {
            Setup();
            settingsService.Settings.M4BFileTreatment = treatment;
            contentManager.SeedBooksToDiscover((MakeAudiobook("Ordered", "/any.m4b"), null));

            await service.Index();

            var converterIdx = callLog.IndexOf("converter:convert");
            var discoveryIdx = callLog.IndexOf("contentManager:GetAllBooks:start");
            Assert.IsGreaterThan(-1, converterIdx, $"Treatment {treatment}: converter did not run");
            Assert.IsGreaterThan(-1, discoveryIdx, $"Treatment {treatment}: discovery did not run");
            Assert.IsGreaterThan(converterIdx, discoveryIdx, $"Treatment {treatment}: converter ran after discovery");
        }
    }

    // ---------- Helpers ----------

    private static Book MakeAudiobook(string title, string sourcePath)
    {
        var uuid = Guid.NewGuid();
        return new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
            Authors = [new Author { AuthorUuid = Guid.NewGuid(), Name = "Some Author" }],
            File = new BookFile
            {
                BookUuid = uuid,
                SourcePath = sourcePath,
                MediaType = MediaType.Audiobook,
                AudioCodec = "aac"
            }
        };
    }

    private static Book MakeMp3FolderAudiobook(string title, string sourcePath)
    {
        var book = MakeAudiobook(title, sourcePath);
        book.File.AudioCodec = "mp3";
        return book;
    }

    // ---------- Fakes ----------

    /// <summary>
    /// Mirrors <c>FileMp3AudiobookConverter</c> at the interface level: it reads the
    /// treatment setting, and for <see cref="M4BFileTreatment.ConvertAtIndex"/> it mutates
    /// the discovery list by swapping the M4B out for an MP3 folder entry.
    /// </summary>
    internal sealed class TreatmentAwareFileConverter(List<string> log, FakeSettingsService settings, FakeFileContentManager contentManager) : IFileConverter
    {
        public (Book M4B, Book Mp3Folder)? SimulatedReplacement { get; set; }
        public M4BFileTreatment? LastObservedTreatment { get; private set; }
        public int ConvertedBookCount { get; private set; }

        public Task<int> Convert(IReadOnlyList<Book> existingBooks, CancellationToken cancellationToken)
        {
            log.Add("converter:convert");
            LastObservedTreatment = settings.Settings.M4BFileTreatment;

            if (LastObservedTreatment == M4BFileTreatment.ConvertAtIndex && SimulatedReplacement is { } replacement)
            {
                contentManager.ReplaceInDiscovery(replacement.M4B.File.SourcePath, replacement.Mp3Folder);
                ConvertedBookCount++;
            }

            return Task.FromResult(0);
        }
    }

    internal sealed class FakeFileContentManager(List<string> log) : IFileContentManager
    {
        private readonly List<(Book Book, byte[]? Cover)> toDiscover = [];

        public void SeedBooksToDiscover(params (Book Book, byte[]? Cover)[] items) => toDiscover.AddRange(items);

        public void ReplaceInDiscovery(string removeSourcePath, Book replacement)
        {
            toDiscover.RemoveAll(x => string.Equals(x.Book.File.SourcePath, removeSourcePath, StringComparison.OrdinalIgnoreCase));
            toDiscover.Add((replacement, null));
        }

        public int GetBookFilesCount() => toDiscover.Count;

        public async IAsyncEnumerable<(Book Metadata, byte[]? CoverBytes)> GetAllBooks()
        {
            log.Add("contentManager:GetAllBooks:start");
            foreach (var item in toDiscover)
            {
                yield return item;
                await Task.Yield();
            }
        }

        public string? SaveCover(string filePath, byte[]? coverBytes, Guid bookUuid) => coverBytes is null ? null : $"/covers/{bookUuid}.jpg";
        public void CleanupStaleDerivatives(List<Book> currentBooks, List<Book>? previousBooks) { }
        public Task CreateDerivativeFiles(Book book) => Task.CompletedTask;
        public Task<Book> GetBook(Book book) => Task.FromResult(book);
        public Task<Book> UpdateBook(Book book, CoverUpdate? coverUpdate = null) => Task.FromResult(book);
        public Task<byte[]?> GetContentFile(Book book) => throw new NotImplementedException();
        public Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<byte[]?> GetCoverFile(Book book, int? width = null, int? height = null, int? quality = null) => throw new NotImplementedException();
    }

    internal sealed class FakeBookRepository : IBookRepository
    {
        private readonly Dictionary<Guid, Book> byUuid = [];

        public IEnumerable<Book> All => byUuid.Values;

        public Task<List<Book>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<Book?> FindByUuid(Guid uuid) => Task.FromResult(byUuid.GetValueOrDefault(uuid));
        public Task<Book?> FindByEntitlementUuid(Guid entitlementUuid) => Task.FromResult(byUuid.Values.FirstOrDefault(b => b.EntitlementUuid == entitlementUuid));
        public Task<Book?> FindByPath(string sourcePath) => Task.FromResult(byUuid.Values.FirstOrDefault(b => string.Equals(b.File.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)));

        public Task Add(Book book)
        {
            byUuid[book.BookUuid] = book;
            return Task.CompletedTask;
        }

        public Task Update(Book book)
        {
            byUuid[book.BookUuid] = book;
            return Task.CompletedTask;
        }

        public Task Remove(Guid uuid)
        {
            byUuid.Remove(uuid);
            return Task.CompletedTask;
        }

        public Task<List<string>> GetPublishers() => Task.FromResult(new List<string>());
    }

    internal sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new() { WordsPerPage = 250 };
        public Task<AppSettings> Get() => Task.FromResult(Settings);
        public Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp)
        {
            Settings.LastIndexedTimestamp = timestamp;
            return Task.CompletedTask;
        }
        public Task UpdateSettings(AppSettings settings)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeAuthorRepository : IAuthorRepository
    {
        private readonly Dictionary<Guid, Author> byUuid = [];
        private readonly Dictionary<Guid, List<Author>> byBook = [];

        public Task<List<Author>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<Author?> FindByUuid(Guid authorUuid) => Task.FromResult(byUuid.GetValueOrDefault(authorUuid));
        public Task<Author?> FindByName(string name) => Task.FromResult(byUuid.Values.FirstOrDefault(a => a.Name == name));

        public Task<Guid> GetOrCreate(string name)
        {
            var existing = byUuid.Values.FirstOrDefault(a => a.Name == name);
            if (existing is not null)
                return Task.FromResult(existing.AuthorUuid);
            var a = new Author { AuthorUuid = Guid.NewGuid(), Name = name };
            byUuid[a.AuthorUuid] = a;
            return Task.FromResult(a.AuthorUuid);
        }

        public Task SetBookAuthors(Guid bookUuid, List<string> authorNames)
        {
            byBook[bookUuid] = authorNames.Select(n => byUuid.Values.First(a => a.Name == n)).ToList();
            return Task.CompletedTask;
        }

        public Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid) => Task.FromResult(byBook.GetValueOrDefault(bookUuid, []));
        public Task RemoveOrphanedAuthors() => Task.CompletedTask;
        public Task Update(Author author) => Task.CompletedTask;
        public Task ReassignBooks(Guid sourceUuid, Guid targetUuid) => Task.CompletedTask;
    }

    internal sealed class FakeSeriesRepository : ISeriesRepository
    {
        public Task<List<Series>> GetAll() => Task.FromResult(new List<Series>());
        public Task<Series?> FindByUuid(Guid seriesUuid) => Task.FromResult<Series?>(null);
        public Task<Series?> FindByName(string name) => Task.FromResult<Series?>(null);
        public Task<Guid> GetOrCreate(string name) => Task.FromResult(Guid.NewGuid());
        public Task SetBookSeries(Guid bookUuid, Guid seriesUuid, decimal? index) => Task.CompletedTask;
        public Task RemoveBookSeries(Guid bookUuid) => Task.CompletedTask;
        public Task<Series?> GetSeriesByBookUuid(Guid bookUuid) => Task.FromResult<Series?>(null);
        public Task RemoveOrphanedSeries() => Task.CompletedTask;
        public Task Update(Series series) => Task.CompletedTask;
        public Task ReassignBooks(Guid sourceUuid, Guid targetUuid) => Task.CompletedTask;
    }

    internal sealed class FakeDatabasePersistence : IDatabasePersistence
    {
        public Task<bool> Flush() => Task.FromResult(true);
    }

    internal sealed class FakeIndexingProgressService : IIndexingProgressService
    {
        public void Subscribe(IProgress<IndexingReport> sink) { }
        public void ReportConverting(string bookTitle, int index, int total) { }
        public void ReportIndexing(string bookTitle, int index, int total) { }
        public void ReportSubTask(string title, int progress, int total) { }
        public void ReportDone() { }
    }
}
