using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.Business.Library.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Business;

[TestClass]
public sealed class FileManagementServiceTests
{
    private FakeBookRepository bookRepository = null!;
    private FakeSettingsService settingsService = null!;
    private FakeFileRenamer ebookRenamer = null!;
    private FakeFileManagementProgressService progressService = null!;
    private FakeDatabasePersistence databasePersistence = null!;
    private FileManagementService service = null!;
    private string tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        bookRepository = new FakeBookRepository();
        settingsService = new FakeSettingsService();
        ebookRenamer = new FakeFileRenamer([".epub"]);
        progressService = new FakeFileManagementProgressService();
        databasePersistence = new FakeDatabasePersistence();
        service = new FileManagementService(bookRepository, settingsService, [ebookRenamer], progressService, databasePersistence, NullLogger<FileManagementService>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public async Task ComputePlan_BookAlreadyMatchingTemplate_IsSkipped()
    {
        settingsService.Settings.FileRenameTemplate = "<Authors> - <Title>";
        var path = Path.Combine(tempDir, "Author - Title.epub");
        bookRepository.Seed(BuildBook(path, "Title", "Author"));

        var plan = await service.ComputePlan();

        Assert.IsEmpty(plan.Operations);
        Assert.HasCount(1, plan.Skipped);
        Assert.IsEmpty(plan.Conflicts);
    }

    [TestMethod]
    public async Task ComputePlan_BookNeedingRename_AppearsInOperations()
    {
        settingsService.Settings.FileRenameTemplate = "<Authors> - <Title>";
        var path = Path.Combine(tempDir, "wrong.epub");
        bookRepository.Seed(BuildBook(path, "Title", "Author"));

        var plan = await service.ComputePlan();

        Assert.HasCount(1, plan.Operations);
        Assert.AreEqual(Path.Combine(tempDir, "Author - Title.epub"), plan.Operations[0].NewPath);
    }

    [TestMethod]
    public async Task ComputePlan_TwoBooksRenderToSamePath_AreFlaggedAsDuplicate()
    {
        settingsService.Settings.FileRenameTemplate = "<Authors> - <Title>";
        bookRepository.Seed(BuildBook(Path.Combine(tempDir, "a.epub"), "Title", "Author"));
        bookRepository.Seed(BuildBook(Path.Combine(tempDir, "b.epub"), "Title", "Author"));

        var plan = await service.ComputePlan();

        Assert.HasCount(1, plan.Conflicts);
        Assert.AreEqual(FileManagementConflictKind.Duplicate, plan.Conflicts[0].Kind);
        Assert.HasCount(2, plan.Conflicts[0].BookUuids);
    }

    [TestMethod]
    public async Task ComputePlan_TargetFileExistsOnDisk_FlaggedAsExistingFileConflict()
    {
        settingsService.Settings.FileRenameTemplate = "<Title>";
        var sourcePath = Path.Combine(tempDir, "wrong.epub");
        var blocker = Path.Combine(tempDir, "Title.epub");
        File.WriteAllText(blocker, "blocker");
        bookRepository.Seed(BuildBook(sourcePath, "Title", "Author"));

        var plan = await service.ComputePlan();

        Assert.IsEmpty(plan.Operations);
        Assert.HasCount(1, plan.Conflicts);
        Assert.AreEqual(FileManagementConflictKind.ExistingFile, plan.Conflicts[0].Kind);
    }

    [TestMethod]
    public async Task ComputePlan_BookWithUnknownExtension_IsSkippedWithoutCrashing()
    {
        settingsService.Settings.FileRenameTemplate = "<Title>";
        bookRepository.Seed(BuildBook(Path.Combine(tempDir, "book.unknown"), "Title", "Author"));

        var plan = await service.ComputePlan();

        Assert.IsEmpty(plan.Operations);
        Assert.IsEmpty(plan.Conflicts);
        Assert.IsEmpty(plan.Skipped);
    }

    [TestMethod]
    public async Task ComputePlan_InvalidTemplate_Throws()
    {
        settingsService.Settings.FileRenameTemplate = "<NoSuch>";
        bookRepository.Seed(BuildBook(Path.Combine(tempDir, "x.epub"), "T", "A"));

        await Assert.ThrowsExactlyAsync<TemplateException>(async () => await service.ComputePlan());
    }

    [TestMethod]
    public async Task Run_WithConflicts_Throws()
    {
        settingsService.Settings.FileRenameTemplate = "<Authors> - <Title>";
        bookRepository.Seed(BuildBook(Path.Combine(tempDir, "a.epub"), "T", "A"));
        bookRepository.Seed(BuildBook(Path.Combine(tempDir, "b.epub"), "T", "A"));

        await Assert.ThrowsExactlyAsync<FileManagementConflictException>(
            async () => await service.Run(CancellationToken.None));
    }

    [TestMethod]
    public async Task Run_HappyPath_ExecutesRenameAndFlushes()
    {
        settingsService.Settings.FileRenameTemplate = "<Title>";
        bookRepository.Seed(BuildBook(Path.Combine(tempDir, "old.epub"), "Renamed", "A"));

        await service.Run(CancellationToken.None);

        Assert.HasCount(1, ebookRenamer.Executed);
        Assert.AreEqual(Path.Combine(tempDir, "Renamed.epub"), ebookRenamer.Executed[0].NewSourcePath);
        Assert.AreEqual(1, databasePersistence.FlushCount);
        Assert.IsNotNull(progressService.Completion);
        Assert.AreEqual(1, progressService.Completion.Renamed);
        Assert.IsEmpty(progressService.Completion.Failed);
    }

    [TestMethod]
    public async Task Run_WhenDbUpdateFails_ReversesFilesystemAndMarksFailed()
    {
        settingsService.Settings.FileRenameTemplate = "<Title>";
        var originalPath = Path.Combine(tempDir, "old.epub");
        bookRepository.Seed(BuildBook(originalPath, "Renamed", "A"));
        bookRepository.UpdateShouldThrow = true;

        await service.Run(CancellationToken.None);

        Assert.HasCount(2, ebookRenamer.Executed);
        Assert.AreEqual(Path.Combine(tempDir, "Renamed.epub"), ebookRenamer.Executed[0].NewSourcePath);
        Assert.AreEqual(originalPath, ebookRenamer.Executed[1].NewSourcePath);
        Assert.IsNotNull(progressService.Completion);
        Assert.AreEqual(0, progressService.Completion.Renamed);
        Assert.HasCount(1, progressService.Completion.Failed);
    }

    private static Book BuildBook(string sourcePath, string title, string author)
    {
        var uuid = Guid.NewGuid();
        return new Book
        {
            BookUuid = uuid,
            Title = title,
            Authors = [new Author { Name = author }],
            File = new BookFile { BookUuid = uuid, SourcePath = sourcePath, MediaType = MediaType.Ebook }
        };
    }

    private sealed class FakeBookRepository : IBookRepository
    {
        private readonly List<Book> books = [];

        public bool UpdateShouldThrow { get; set; }

        public void Seed(Book book) => books.Add(book);

        public Task<List<Book>> GetAll() => Task.FromResult(books.ToList());
        public Task<Book?> FindByUuid(Guid uuid) => Task.FromResult(books.FirstOrDefault(b => b.BookUuid == uuid));
        public Task<Book?> FindByEntitlementUuid(Guid uuid) => throw new NotSupportedException();
        public Task<Book?> FindByPath(string sourcePath) => throw new NotSupportedException();
        public Task Add(Book book) { books.Add(book); return Task.CompletedTask; }

        public Task Update(Book book)
        {
            if (UpdateShouldThrow)
                throw new InvalidOperationException("Simulated DB failure.");
            var index = books.FindIndex(b => b.BookUuid == book.BookUuid);
            if (index >= 0)
                books[index] = book;
            return Task.CompletedTask;
        }

        public Task Remove(Guid uuid) => Task.CompletedTask;
        public Task<List<string>> GetPublishers() => Task.FromResult(new List<string>());
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new();
        public Task<AppSettings> Get() => Task.FromResult(Settings);
        public Task UpdateLastIndexedTimestamp(DateTimeOffset timestamp) => Task.CompletedTask;
        public Task UpdateSettings(AppSettings settings) { Settings = settings; return Task.CompletedTask; }
    }

    private sealed class FakeFileRenamer(string[] extensions) : IFileRenamer
    {
        public List<FileRenameStep> Executed { get; } = [];
        public Func<FileRenameStep, bool>? FailExecutionWhen { get; set; }

        public string[] SupportedExtensions { get; } = extensions;

        public FileRenameStep PlanRename(Book book, string newBaseName)
        {
            var directory = Path.GetDirectoryName(book.File.SourcePath) ?? string.Empty;
            var extension = Path.GetExtension(book.File.SourcePath);
            return new FileRenameStep
            {
                OldSourcePath = book.File.SourcePath,
                NewSourcePath = Path.Combine(directory, newBaseName + extension)
            };
        }

        public void Execute(FileRenameStep step, IProgress<ProgressTask>? subProgress = null)
        {
            if (FailExecutionWhen is not null && FailExecutionWhen(step))
                throw new IOException("Simulated IO failure.");
            Executed.Add(step);
        }
    }

    private sealed class FakeFileManagementProgressService : IProgressService
    {
        public List<ProgressReport> Reports { get; } = [];
        public FileManagementCompletion? Completion { get; private set; }

        public void Subscribe(IProgress<object> sink) { }

        public void Report(string title, int count, int total)
        {
            Reports.Add(new ProgressReport { Task = new ProgressTask { Title = title, Count = count, Total = total } });
        }

        public void ReportSubTask(string title, int count, int total)
        {
            if (Reports.Count == 0)
                return;
            var outer = Reports[^1].Task;
            Reports.Add(new ProgressReport { Task = outer, SubTask = new ProgressTask { Title = title, Count = count, Total = total } });
        }

        public void ReportDone<T>(T doneObject)
        {
            if (doneObject is FileManagementCompletion c)
                Completion = c;
        }
    }

    private sealed class FakeDatabasePersistence : IDatabasePersistence
    {
        public int FlushCount { get; private set; }
        public Task<bool> Flush() { FlushCount++; return Task.FromResult(true); }
    }
}
