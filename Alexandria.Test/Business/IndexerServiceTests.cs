using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.BusinessLayer;

[TestClass]
public sealed class IndexerServiceTests
{
    private FakeFileContentManager contentManager = null!;
    private FakeBookRepository bookRepository = null!;
    private FakeSettingsService settingsService = null!;
    private FakeAuthorRepository authorRepository = null!;
    private FakeSeriesRepository seriesRepository = null!;
    private FakeDatabasePersistence databasePersistence = null!;
    private RecordingFileConverter converter = null!;
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
        converter = new RecordingFileConverter(callLog);
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

    // ---------- Index ----------

    [TestMethod]
    public async Task Index_EmptyLibrary_NoOpButFinalizesProgress()
    {
        var count = await service.Index();

        Assert.AreEqual(0, count);
        Assert.IsEmpty(bookRepository.All);
        Assert.AreEqual(1, settingsService.UpdateLastIndexedTimestampCount);
        Assert.AreEqual(1, databasePersistence.FlushCount);
        Assert.AreEqual(1, progressService.DoneCount);
    }

    [TestMethod]
    public async Task Index_NewBook_PersistedAndDerivativesCreated()
    {
        var book = MakeEbook("New Book", "/books/new.epub");
        contentManager.SeedBooksToDiscover((book, [1, 2, 3]));

        var count = await service.Index();

        Assert.AreEqual(1, count);
        var stored = bookRepository.All.Single();
        Assert.AreEqual("New Book", stored.Title);
        Assert.AreNotEqual(Guid.Empty, stored.EntitlementUuid);
        Assert.AreEqual(1, contentManager.CreateDerivativeFilesCalls.Count);
        Assert.AreEqual(1, contentManager.SaveCoverCalls.Count);
    }

    [TestMethod]
    public async Task Index_NewBook_EnsuresAuthorAndSeriesCreated()
    {
        var book = MakeEbook("Bookish", "/books/a.epub", authorName: "Lindgren, Astrid", seriesName: "Pippi", seriesIndex: 1);
        contentManager.SeedBooksToDiscover((book, null));

        await service.Index();

        Assert.IsTrue(authorRepository.GetOrCreateNames.Contains("Lindgren, Astrid"));
        Assert.IsTrue(seriesRepository.GetOrCreateNames.Contains("Pippi"));
    }

    [TestMethod]
    public async Task Index_UnchangedBook_TakesFastPathSkippingUpdate()
    {
        var book = MakeEbook("Stable", "/books/stable.epub");
        bookRepository.Seed(book);
        // Yield a clone with identical metadata — MetadataEquals returns true, no cover bytes → CoverUnchanged returns true.
        var discovered = Clone(book);
        contentManager.SeedBooksToDiscover((discovered, null));

        await service.Index();

        Assert.AreEqual(0, bookRepository.UpdateCallCount);
        Assert.AreEqual(0, bookRepository.AddCallCount);
        Assert.IsTrue(contentManager.CreateDerivativeFilesCalls.Contains(book.BookUuid));
    }

    [TestMethod]
    public async Task Index_ChangedMetadata_UpdatesModifiedAtPreservesCreatedAt()
    {
        var original = DateTimeOffset.UtcNow.AddDays(-30);
        var existing = MakeEbook("Original", "/books/x.epub", createdAt: original, modifiedAt: original);
        bookRepository.Seed(existing);

        var updated = Clone(existing);
        updated.Title = "Changed";
        contentManager.SeedBooksToDiscover((updated, null));

        await service.Index();

        var stored = bookRepository.All.Single();
        Assert.AreEqual("Changed", stored.Title);
        Assert.AreEqual(existing.EntitlementUuid, stored.EntitlementUuid);
        Assert.AreEqual(original, stored.CreatedAt);
        Assert.IsGreaterThan(original, stored.ModifiedAt);
        Assert.AreEqual(1, bookRepository.UpdateCallCount);
    }

    [TestMethod]
    public async Task Index_MovedBook_PreservesBookUuidAndEntitlementUuid()
    {
        var existing = MakeEbook("Traveler", "/old-location/trip.epub");
        bookRepository.Seed(existing);

        var moved = Clone(existing);
        moved.File.SourcePath = "/new-location/trip.epub";
        contentManager.SeedBooksToDiscover((moved, null));

        await service.Index();

        var stored = bookRepository.All.Single();
        Assert.AreEqual(existing.BookUuid, stored.BookUuid);
        Assert.AreEqual(existing.EntitlementUuid, stored.EntitlementUuid);
        Assert.AreEqual("/new-location/trip.epub", stored.File.SourcePath);
    }

    [TestMethod]
    public async Task Index_StaleBook_RemovedAndOrphansCleaned()
    {
        var stale = MakeEbook("Stale", "/books/gone.epub");
        bookRepository.Seed(stale);
        // Nothing discovered on this pass.

        await service.Index();

        Assert.IsFalse(bookRepository.All.Any(b => b.BookUuid == stale.BookUuid));
        Assert.AreEqual(1, bookRepository.RemoveCallCount);
        Assert.AreEqual(1, authorRepository.RemoveOrphanedCallCount);
        Assert.AreEqual(1, seriesRepository.RemoveOrphanedCallCount);
    }

    [TestMethod]
    public async Task Index_NoStaleBooks_DoesNotRemoveOrphans()
    {
        var book = MakeEbook("Keeps", "/books/keeps.epub");
        bookRepository.Seed(book);
        contentManager.SeedBooksToDiscover((Clone(book), null));

        await service.Index();

        Assert.AreEqual(0, authorRepository.RemoveOrphanedCallCount);
        Assert.AreEqual(0, seriesRepository.RemoveOrphanedCallCount);
    }

    [TestMethod]
    public async Task Index_ConvertersRunBeforeFileDiscovery()
    {
        var book = MakeEbook("OrderTest", "/books/order.epub");
        contentManager.SeedBooksToDiscover((book, null));

        await service.Index();

        var converterIndex = callLog.IndexOf("converter:convert");
        var discoveryIndex = callLog.IndexOf("contentManager:GetAllBooks:start");
        Assert.IsGreaterThan(-1, converterIndex);
        Assert.IsGreaterThan(-1, discoveryIndex);
        Assert.IsGreaterThan(converterIndex, discoveryIndex);
    }

    [TestMethod]
    public async Task Index_UpdatesLastIndexedTimestampAndFlushesDatabase()
    {
        await service.Index();

        Assert.AreEqual(1, settingsService.UpdateLastIndexedTimestampCount);
        Assert.AreEqual(1, databasePersistence.FlushCount);
        Assert.IsNotNull(settingsService.Settings.LastIndexedTimestamp);
    }

    [TestMethod]
    public async Task Index_ReportsProgressForEachDiscoveredBook()
    {
        contentManager.SeedBooksToDiscover(
            (MakeEbook("A", "/a.epub"), null),
            (MakeEbook("B", "/b.epub"), null),
            (MakeEbook("C", "/c.epub"), null));

        await service.Index();

        Assert.HasCount(3, progressService.IndexingReports);
        Assert.AreEqual("A", progressService.IndexingReports[0].Title);
        Assert.AreEqual("C", progressService.IndexingReports[2].Title);
        Assert.AreEqual(1, progressService.DoneCount);
    }

    [TestMethod]
    public async Task Index_ComputesPageCountFromWordsPerPageSetting()
    {
        settingsService.Settings.WordsPerPage = 100;
        var book = MakeEbook("Wordy", "/w.epub");
        book.File.WordCount = 1000;
        contentManager.SeedBooksToDiscover((book, null));

        await service.Index();

        var stored = bookRepository.All.Single();
        Assert.AreEqual(10, stored.File.PageCount);
    }

    [TestMethod]
    public async Task Index_CleansUpStaleDerivatives()
    {
        var book = MakeEbook("Cleanup", "/c.epub");
        contentManager.SeedBooksToDiscover((book, null));

        await service.Index();

        Assert.AreEqual(1, contentManager.CleanupStaleDerivativesCallCount);
    }

    // ---------- ReindexBook ----------

    [TestMethod]
    public async Task ReindexBook_WithoutMetadataOrCover_ReExtractsAndPersists()
    {
        var existing = MakeEbook("ToReindex", "/r.epub");
        bookRepository.Seed(existing);
        contentManager.OnGetBook = book => Clone(book);

        await service.ReindexBook(existing.BookUuid);

        Assert.IsEmpty(contentManager.UpdateBookCalls);
        Assert.AreEqual(1, contentManager.GetBookCallCount);
        Assert.AreEqual(1, bookRepository.UpdateCallCount);
        Assert.AreEqual(1, databasePersistence.FlushCount);
    }

    [TestMethod]
    public async Task ReindexBook_WithDesiredMetadata_EmbedsBeforeGetBook()
    {
        var existing = MakeEbook("Before", "/r.epub");
        bookRepository.Seed(existing);
        contentManager.OnGetBook = book => Clone(book);
        var desired = Clone(existing);
        desired.Title = "After";

        await service.ReindexBook(existing.BookUuid, desiredMetadata: desired);

        Assert.HasCount(1, contentManager.UpdateBookCalls);
        Assert.AreEqual("After", contentManager.UpdateBookCalls[0].Book.Title);
        // Embed should happen before GetBook (re-extract)
        var embedIdx = callLog.IndexOf("contentManager:UpdateBook");
        var extractIdx = callLog.IndexOf("contentManager:GetBook");
        Assert.IsGreaterThan(-1, embedIdx);
        Assert.IsGreaterThan(-1, extractIdx);
        Assert.IsGreaterThan(embedIdx, extractIdx);
    }

    [TestMethod]
    public async Task ReindexBook_WithCoverUpdate_PassesCoverUpdateToContentManager()
    {
        var existing = MakeEbook("CoverEdit", "/c.epub");
        bookRepository.Seed(existing);
        contentManager.OnGetBook = book => Clone(book);
        var cover = new CoverUpdate(CoverData: [1, 2, 3], MediaType: "image/jpeg");

        await service.ReindexBook(existing.BookUuid, coverUpdate: cover);

        Assert.HasCount(1, contentManager.UpdateBookCalls);
        Assert.AreSame(cover, contentManager.UpdateBookCalls[0].CoverUpdate);
    }

    [TestMethod]
    public async Task ReindexBook_PreservesEntitlementUuidAndCreatedAt()
    {
        var original = DateTimeOffset.UtcNow.AddDays(-90);
        var existing = MakeEbook("Preserve", "/p.epub", createdAt: original, modifiedAt: original);
        bookRepository.Seed(existing);
        contentManager.OnGetBook = book =>
        {
            var fresh = Clone(book);
            // Simulate re-extract returning a Book with different EntitlementUuid/CreatedAt
            fresh.EntitlementUuid = Guid.NewGuid();
            fresh.CreatedAt = DateTimeOffset.UtcNow;
            return fresh;
        };

        await service.ReindexBook(existing.BookUuid);

        var stored = bookRepository.All.Single();
        Assert.AreEqual(existing.EntitlementUuid, stored.EntitlementUuid);
        Assert.AreEqual(original, stored.CreatedAt);
        Assert.IsGreaterThan(original, stored.ModifiedAt);
    }

    [TestMethod]
    public async Task ReindexBook_Audiobook_PreservesLanguageIdentifiersAndDublinCoreFields()
    {
        var existing = MakeAudiobook("Heard", "/a.m4b");
        existing.Language = "sv";
        existing.Identifiers = ["isbn:9780000000001"];
        existing.Source = "original-source";
        existing.Type = "type-x";
        existing.Relation = "related-y";
        existing.Coverage = "region-z";
        existing.Format = "format-q";
        bookRepository.Seed(existing);
        contentManager.OnGetBook = book =>
        {
            var fresh = Clone(book);
            // Simulate re-extract that would normally lose Dublin Core fields
            fresh.Language = "en";
            fresh.Identifiers = [];
            fresh.Source = null;
            fresh.Type = null;
            fresh.Relation = null;
            fresh.Coverage = null;
            fresh.Format = null;
            return fresh;
        };

        await service.ReindexBook(existing.BookUuid);

        var stored = bookRepository.All.Single();
        Assert.AreEqual("sv", stored.Language);
        Assert.HasCount(1, stored.Identifiers);
        Assert.AreEqual("original-source", stored.Source);
        Assert.AreEqual("type-x", stored.Type);
        Assert.AreEqual("related-y", stored.Relation);
        Assert.AreEqual("region-z", stored.Coverage);
        Assert.AreEqual("format-q", stored.Format);
    }

    [TestMethod]
    public async Task ReindexBook_Ebook_DoesNotRestoreDublinCoreFieldsFromDb()
    {
        var existing = MakeEbook("ReadAlong", "/e.epub");
        existing.Source = "db-source";
        bookRepository.Seed(existing);
        contentManager.OnGetBook = book =>
        {
            var fresh = Clone(book);
            fresh.Source = null; // simulate re-extract finding empty DC
            return fresh;
        };

        await service.ReindexBook(existing.BookUuid);

        var stored = bookRepository.All.Single();
        // Ebooks round-trip all DC fields, so re-extracted null stays null
        Assert.IsNull(stored.Source);
    }

    [TestMethod]
    public async Task ReindexBook_RespectsWordsPerPageSetting()
    {
        settingsService.Settings.WordsPerPage = 200;
        var existing = MakeEbook("PageCalc", "/p.epub");
        bookRepository.Seed(existing);
        contentManager.OnGetBook = book =>
        {
            var fresh = Clone(book);
            fresh.File.WordCount = 1000;
            return fresh;
        };

        await service.ReindexBook(existing.BookUuid);

        var stored = bookRepository.All.Single();
        Assert.AreEqual(5, stored.File.PageCount);
    }

    [TestMethod]
    public async Task ReindexBook_UnknownUuid_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await service.ReindexBook(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task ReindexBook_PreservesSubtitleFromExistingRegardlessOfMediaType()
    {
        var existing = MakeEbook("Subtitled", "/s.epub");
        existing.Subtitle = "The Original Subtitle";
        bookRepository.Seed(existing);
        contentManager.OnGetBook = book =>
        {
            var fresh = Clone(book);
            fresh.Subtitle = null;
            return fresh;
        };

        await service.ReindexBook(existing.BookUuid);

        Assert.AreEqual("The Original Subtitle", bookRepository.All.Single().Subtitle);
    }

    // ---------- Helpers ----------

    private static Book MakeEbook(string title, string sourcePath, string? authorName = "Default Author", string? seriesName = null, decimal? seriesIndex = null, DateTimeOffset? createdAt = null, DateTimeOffset? modifiedAt = null)
    {
        var uuid = Guid.NewGuid();
        var book = new Book
        {
            BookUuid = uuid,
            EntitlementUuid = Guid.NewGuid(),
            Title = title,
            Language = "en",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ModifiedAt = modifiedAt ?? DateTimeOffset.UtcNow,
            Authors = authorName is null ? [] : [new Author { AuthorUuid = Guid.NewGuid(), Name = authorName }],
            File = new BookFile { BookUuid = uuid, SourcePath = sourcePath, MediaType = MediaType.Ebook }
        };
        if (seriesName is not null)
            book.Series = new Series { SeriesUuid = Guid.NewGuid(), Name = seriesName, Index = seriesIndex };
        return book;
    }

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
            Authors = [new Author { AuthorUuid = Guid.NewGuid(), Name = "Narrator" }],
            File = new BookFile { BookUuid = uuid, SourcePath = sourcePath, MediaType = MediaType.Audiobook }
        };
    }

    private static Book Clone(Book source)
    {
        var copy = new Book
        {
            BookUuid = source.BookUuid,
            EntitlementUuid = source.EntitlementUuid,
            Title = source.Title,
            Subtitle = source.Subtitle,
            Language = source.Language,
            Isbn = source.Isbn,
            PublicationDate = source.PublicationDate,
            Description = source.Description,
            Publisher = source.Publisher,
            Identifiers = [.. source.Identifiers],
            Subjects = [.. source.Subjects],
            Rights = source.Rights,
            Source = source.Source,
            Type = source.Type,
            Relation = source.Relation,
            Coverage = source.Coverage,
            Format = source.Format,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsArchived = source.IsArchived,
            Authors = [.. source.Authors.Select(a => new Author { AuthorUuid = a.AuthorUuid, Name = a.Name })],
            Contributors = [.. source.Contributors],
            Chapters = [.. source.Chapters],
            File = new BookFile
            {
                BookUuid = source.File.BookUuid,
                MediaType = source.File.MediaType,
                SourcePath = source.File.SourcePath,
                CoverFilePath = source.File.CoverFilePath,
                KepubFilePath = source.File.KepubFilePath,
                TotalFileSize = source.File.TotalFileSize,
                PageCount = source.File.PageCount,
                WordCount = source.File.WordCount,
                Duration = source.File.Duration,
                IsAbridged = source.File.IsAbridged,
                AudioCodec = source.File.AudioCodec,
                AudioBitrate = source.File.AudioBitrate
            }
        };
        if (source.Series is not null)
            copy.Series = new Series { SeriesUuid = source.Series.SeriesUuid, Name = source.Series.Name, Index = source.Series.Index };
        return copy;
    }

    // ---------- Fakes ----------

    internal sealed class FakeFileContentManager(List<string> log) : IFileContentManager
    {
        private readonly List<(Book Book, byte[]? Cover)> toDiscover = [];

        public Func<Book, Book>? OnGetBook { get; set; }

        public List<Guid> CreateDerivativeFilesCalls { get; } = [];
        public List<Guid> SaveCoverCalls { get; } = [];
        public List<(Book Book, CoverUpdate? CoverUpdate)> UpdateBookCalls { get; } = [];
        public int GetBookCallCount { get; private set; }
        public int CleanupStaleDerivativesCallCount { get; private set; }

        public void SeedBooksToDiscover(params (Book Book, byte[]? Cover)[] items) => toDiscover.AddRange(items);

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

        public string? SaveCover(string filePath, byte[]? coverBytes, Guid bookUuid)
        {
            SaveCoverCalls.Add(bookUuid);
            return coverBytes is null ? null : $"/covers/{bookUuid}.jpg";
        }

        public void CleanupStaleDerivatives(List<Book> currentBooks, List<Book>? previousBooks) => CleanupStaleDerivativesCallCount++;

        public Task CreateDerivativeFiles(Book book)
        {
            CreateDerivativeFilesCalls.Add(book.BookUuid);
            return Task.CompletedTask;
        }

        public Task<Book> GetBook(Book book)
        {
            log.Add("contentManager:GetBook");
            GetBookCallCount++;
            var result = OnGetBook is null ? book : OnGetBook(book);
            return Task.FromResult(result);
        }

        public Task<Book> UpdateBook(Book book, CoverUpdate? coverUpdate = null)
        {
            log.Add("contentManager:UpdateBook");
            UpdateBookCalls.Add((book, coverUpdate));
            return Task.FromResult(book);
        }

        public Task<byte[]?> GetContentFile(Book book) => throw new NotImplementedException();
        public Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<byte[]?> GetCoverFile(Book book, int? width = null, int? height = null, int? quality = null) => throw new NotImplementedException();
    }

    internal sealed class FakeBookRepository : IBookRepository
    {
        private readonly Dictionary<Guid, Book> byUuid = [];

        public int AddCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int RemoveCallCount { get; private set; }

        public IEnumerable<Book> All => byUuid.Values;

        public void Seed(params Book[] books)
        {
            foreach (var b in books)
                byUuid[b.BookUuid] = b;
        }

        public Task<List<Book>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<Book?> FindByUuid(Guid uuid) => Task.FromResult(byUuid.GetValueOrDefault(uuid));
        public Task<Book?> FindByEntitlementUuid(Guid entitlementUuid) => Task.FromResult(byUuid.Values.FirstOrDefault(b => b.EntitlementUuid == entitlementUuid));
        public Task<Book?> FindByPath(string sourcePath) => Task.FromResult(byUuid.Values.FirstOrDefault(b => string.Equals(b.File.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)));

        public Task Add(Book book)
        {
            AddCallCount++;
            byUuid[book.BookUuid] = book;
            return Task.CompletedTask;
        }

        public Task Update(Book book)
        {
            UpdateCallCount++;
            byUuid[book.BookUuid] = book;
            return Task.CompletedTask;
        }

        public Task Remove(Guid uuid)
        {
            RemoveCallCount++;
            byUuid.Remove(uuid);
            return Task.CompletedTask;
        }

        public Task<List<string>> GetPublishers() => Task.FromResult(new List<string>());
    }

    internal sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new() { WordsPerPage = 250 };
        public int UpdateLastIndexedTimestampCount { get; private set; }

        public Task<AppSettings> Get() => Task.FromResult(Settings);

        public Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp)
        {
            UpdateLastIndexedTimestampCount++;
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

        public int RemoveOrphanedCallCount { get; private set; }
        public List<string> GetOrCreateNames { get; } = [];

        public Task<List<Author>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<Author?> FindByUuid(Guid authorUuid) => Task.FromResult(byUuid.GetValueOrDefault(authorUuid));
        public Task<Author?> FindByName(string name) => Task.FromResult(byUuid.Values.FirstOrDefault(a => a.Name == name));

        public Task<Guid> GetOrCreate(string name)
        {
            GetOrCreateNames.Add(name);
            var existing = byUuid.Values.FirstOrDefault(a => a.Name == name);
            if (existing is not null)
                return Task.FromResult(existing.AuthorUuid);
            var a = new Author { AuthorUuid = Guid.NewGuid(), Name = name };
            byUuid[a.AuthorUuid] = a;
            return Task.FromResult(a.AuthorUuid);
        }

        public Task SetBookAuthors(Guid bookUuid, List<string> authorNames)
        {
            var list = authorNames.Select(n => byUuid.Values.First(a => a.Name == n)).ToList();
            byBook[bookUuid] = list;
            return Task.CompletedTask;
        }

        public Task<List<Author>> GetAuthorsByBookUuid(Guid bookUuid) => Task.FromResult(byBook.GetValueOrDefault(bookUuid, []));

        public Task RemoveOrphanedAuthors()
        {
            RemoveOrphanedCallCount++;
            return Task.CompletedTask;
        }

        public Task Update(Author author) => Task.CompletedTask;
        public Task ReassignBooks(Guid sourceUuid, Guid targetUuid) => Task.CompletedTask;
    }

    internal sealed class FakeSeriesRepository : ISeriesRepository
    {
        private readonly Dictionary<Guid, Series> byUuid = [];
        private readonly Dictionary<Guid, (Guid SeriesUuid, decimal? Index)> byBook = [];

        public int RemoveOrphanedCallCount { get; private set; }
        public List<string> GetOrCreateNames { get; } = [];

        public Task<List<Series>> GetAll() => Task.FromResult(byUuid.Values.ToList());
        public Task<Series?> FindByUuid(Guid seriesUuid) => Task.FromResult(byUuid.GetValueOrDefault(seriesUuid));
        public Task<Series?> FindByName(string name) => Task.FromResult(byUuid.Values.FirstOrDefault(s => s.Name == name));

        public Task<Guid> GetOrCreate(string name)
        {
            GetOrCreateNames.Add(name);
            var existing = byUuid.Values.FirstOrDefault(s => s.Name == name);
            if (existing is not null)
                return Task.FromResult(existing.SeriesUuid);
            var s = new Series { SeriesUuid = Guid.NewGuid(), Name = name };
            byUuid[s.SeriesUuid] = s;
            return Task.FromResult(s.SeriesUuid);
        }

        public Task SetBookSeries(Guid bookUuid, Guid seriesUuid, decimal? index)
        {
            byBook[bookUuid] = (seriesUuid, index);
            return Task.CompletedTask;
        }

        public Task RemoveBookSeries(Guid bookUuid)
        {
            byBook.Remove(bookUuid);
            return Task.CompletedTask;
        }

        public Task<Series?> GetSeriesByBookUuid(Guid bookUuid)
        {
            if (byBook.TryGetValue(bookUuid, out var entry) && byUuid.TryGetValue(entry.SeriesUuid, out var series))
                return Task.FromResult<Series?>(new Series { SeriesUuid = series.SeriesUuid, Name = series.Name, Index = entry.Index });
            return Task.FromResult<Series?>(null);
        }

        public Task RemoveOrphanedSeries()
        {
            RemoveOrphanedCallCount++;
            return Task.CompletedTask;
        }

        public Task Update(Series series) => Task.CompletedTask;
        public Task ReassignBooks(Guid sourceUuid, Guid targetUuid) => Task.CompletedTask;
    }

    internal sealed class FakeDatabasePersistence : IDatabasePersistence
    {
        public int FlushCount { get; private set; }

        public Task<bool> Flush()
        {
            FlushCount++;
            return Task.FromResult(true);
        }
    }

    internal sealed class RecordingFileConverter(List<string> log) : IFileConverter
    {
        public int ConvertCallCount { get; private set; }

        public Task Convert(IReadOnlyList<Book> existingBooks, CancellationToken cancellationToken)
        {
            log.Add("converter:convert");
            ConvertCallCount++;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeIndexingProgressService : IIndexingProgressService
    {
        public List<IndexingReport> IndexingReports { get; } = [];
        public int DoneCount { get; private set; }

        public void Subscribe(IProgress<IndexingReport> sink) { }

        public void ReportConverting(string bookTitle, int index, int total) { }

        public void ReportIndexing(string bookTitle, int index, int total)
        {
            IndexingReports.Add(new IndexingReport { Title = bookTitle, Progress = index, Total = total });
        }

        public void ReportSubTask(string title, int progress, int total) { }

        public void ReportDone() => DoneCount++;
    }
}
