using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class BookRepositoryTests : DatabaseTestBase
{
    private AuthorRepository authorRepository = null!;
    private SeriesRepository seriesRepository = null!;
    private BookRepository repository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        authorRepository = new AuthorRepository(ConnectionProvider);
        seriesRepository = new SeriesRepository(ConnectionProvider);
        repository = new BookRepository(ConnectionProvider, authorRepository, seriesRepository);
    }

    [TestMethod]
    public async Task BookRepository_Add_RoundTripsAllPersistedFields()
    {
        var book = BuildBook();

        await repository.Add(book);

        var fetched = await repository.FindByUuid(book.BookUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(book.Title, fetched.Title);
        Assert.AreEqual(book.Language, fetched.Language);
        Assert.AreEqual(book.Isbn, fetched.Isbn);
        Assert.AreEqual(book.Publisher, fetched.Publisher);
        Assert.AreEqual(book.Description, fetched.Description);
        Assert.AreEqual(book.EntitlementUuid, fetched.EntitlementUuid);
        Assert.AreEqual(book.IsArchived, fetched.IsArchived);
    }

    [TestMethod]
    public async Task BookRepository_Add_PersistsIdentifiersSubjectsAndContributors()
    {
        var book = BuildBook();
        book.Identifiers = ["9780000000001", "urn:isbn:9780000000001"];
        book.Subjects = ["Fiction", "Fantasy"];
        book.Contributors = [new Contributor { Name = "Narrator X", Role = "Narrator" }];

        await repository.Add(book);

        var fetched = await repository.FindByUuid(book.BookUuid);
        Assert.IsNotNull(fetched);
        Assert.HasCount(2, fetched.Identifiers);
        Assert.Contains("9780000000001", fetched.Identifiers);
        Assert.Contains("urn:isbn:9780000000001", fetched.Identifiers);
        Assert.HasCount(2, fetched.Subjects);
        Assert.Contains("Fiction", fetched.Subjects);
        Assert.Contains("Fantasy", fetched.Subjects);
        Assert.HasCount(1, fetched.Contributors);
        Assert.AreEqual("Narrator X", fetched.Contributors[0].Name);
        Assert.AreEqual("Narrator", fetched.Contributors[0].Role);
    }

    [TestMethod]
    public async Task BookRepository_Add_AudiobookWithChapters_PersistsChapters()
    {
        var book = BuildBook(mediaType: MediaType.Audiobook);
        book.Chapters =
        [
            new AudiobookChapter { ChapterIndex = 0, Title = "Chapter 1", Duration = 10, StartTime = 0, EndTime = 10, FileName = "1.mp3" },
            new AudiobookChapter { ChapterIndex = 1, Title = "Chapter 2", Duration = 12, StartTime = 10, EndTime = 22, FileName = "2.mp3" }
        ];

        await repository.Add(book);

        var fetched = await repository.FindByUuid(book.BookUuid);
        Assert.IsNotNull(fetched);
        Assert.HasCount(2, fetched.Chapters);
        Assert.AreEqual("Chapter 1", fetched.Chapters[0].Title);
        Assert.AreEqual("1.mp3", fetched.Chapters[0].FileName);
        Assert.AreEqual(22, fetched.Chapters[1].EndTime);
    }

    [TestMethod]
    public async Task BookRepository_Add_EbookWithoutChapters_LeavesChaptersEmpty()
    {
        var book = BuildBook(mediaType: MediaType.Ebook);

        await repository.Add(book);

        var fetched = await repository.FindByUuid(book.BookUuid);
        Assert.IsNotNull(fetched);
        Assert.IsEmpty(fetched.Chapters);
    }

    [TestMethod]
    public async Task BookRepository_Add_PersistsBookFileSubRow()
    {
        var book = BuildBook(mediaType: MediaType.Audiobook);

        await repository.Add(book);

        var fetched = await repository.FindByUuid(book.BookUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(book.File.SourcePath, fetched.File.SourcePath);
        Assert.AreEqual(book.File.MediaType, fetched.File.MediaType);
        Assert.AreEqual(book.File.TotalFileSize, fetched.File.TotalFileSize);
        Assert.AreEqual(book.File.Duration, fetched.File.Duration);
        Assert.AreEqual(book.File.AudioCodec, fetched.File.AudioCodec);
        Assert.AreEqual(book.File.AudioBitrate, fetched.File.AudioBitrate);
    }

    [TestMethod]
    public async Task BookRepository_Update_PersistsChangesToScalarFields()
    {
        var book = BuildBook(title: "Old Title");
        await repository.Add(book);

        book.Title = "New Title";
        book.Publisher = "Different Publisher";
        book.IsArchived = true;
        await repository.Update(book);

        var fetched = await repository.FindByUuid(book.BookUuid);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("New Title", fetched.Title);
        Assert.AreEqual("Different Publisher", fetched.Publisher);
        Assert.IsTrue(fetched.IsArchived);
    }

    [TestMethod]
    public async Task BookRepository_Update_ReplacesSubTableRows()
    {
        var book = BuildBook();
        book.Subjects = ["Fiction"];
        book.Identifiers = ["9780000000001"];
        await repository.Add(book);

        book.Subjects = ["Science Fiction", "Space Opera"];
        book.Identifiers = ["9780000000002"];
        await repository.Update(book);

        var fetched = await repository.FindByUuid(book.BookUuid);
        Assert.IsNotNull(fetched);
        Assert.HasCount(2, fetched.Subjects);
        Assert.Contains("Science Fiction", fetched.Subjects);
        Assert.Contains("Space Opera", fetched.Subjects);
        Assert.HasCount(1, fetched.Identifiers);
        Assert.AreEqual("9780000000002", fetched.Identifiers[0]);
    }

    [TestMethod]
    public async Task BookRepository_FindByUuid_MissingUuid_ReturnsNull()
    {
        Assert.IsNull(await repository.FindByUuid(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task BookRepository_FindByEntitlementUuid_ReturnsBook()
    {
        var book = BuildBook();
        await repository.Add(book);

        var fetched = await repository.FindByEntitlementUuid(book.EntitlementUuid);

        Assert.IsNotNull(fetched);
        Assert.AreEqual(book.BookUuid, fetched.BookUuid);
    }

    [TestMethod]
    public async Task BookRepository_FindByPath_CaseInsensitiveMatch()
    {
        var book = BuildBook(sourcePath: "/tmp/Some/Book.epub");
        await repository.Add(book);

        var fetched = await repository.FindByPath("/tmp/some/book.epub");

        Assert.IsNotNull(fetched);
        Assert.AreEqual(book.BookUuid, fetched.BookUuid);
    }

    [TestMethod]
    public async Task BookRepository_GetAll_ReturnsAllAddedBooks()
    {
        await repository.Add(BuildBook(title: "A"));
        await repository.Add(BuildBook(title: "B"));
        await repository.Add(BuildBook(title: "C"));

        var all = await repository.GetAll();

        Assert.HasCount(3, all);
    }

    [TestMethod]
    public async Task BookRepository_GetAll_PopulatesAuthorsAndSeriesViaRepositories()
    {
        var book = BuildBook();
        await repository.Add(book);
        await authorRepository.SetBookAuthors(book.BookUuid, ["First Author", "Second Author"]);
        var seriesUuid = await seriesRepository.GetOrCreate("My Series");
        await seriesRepository.SetBookSeries(book.BookUuid, seriesUuid, 3m);

        var fetched = (await repository.GetAll()).Single();

        Assert.HasCount(2, fetched.Authors);
        Assert.IsNotNull(fetched.Series);
        Assert.AreEqual("My Series", fetched.Series.Name);
        Assert.AreEqual(3m, fetched.Series.Index);
    }

    [TestMethod]
    public async Task BookRepository_Remove_DeletesBookAndAllSubTableRows()
    {
        var book = BuildBook();
        book.Identifiers = ["9780000000001"];
        book.Subjects = ["Fiction"];
        book.Contributors = [new Contributor { Name = "Narrator", Role = "Narrator" }];
        await repository.Add(book);
        await authorRepository.SetBookAuthors(book.BookUuid, ["Author"]);
        var seriesUuid = await seriesRepository.GetOrCreate("Series");
        await seriesRepository.SetBookSeries(book.BookUuid, seriesUuid, 1m);

        await repository.Remove(book.BookUuid);

        Assert.IsNull(await repository.FindByUuid(book.BookUuid));
        Assert.IsEmpty(await authorRepository.GetAuthorsByBookUuid(book.BookUuid));
        Assert.IsNull(await seriesRepository.GetSeriesByBookUuid(book.BookUuid));
    }

    [TestMethod]
    public async Task BookRepository_GetPublishers_ReturnsDistinctNonEmptySorted()
    {
        await repository.Add(BuildBook(title: "A", publisher: "Penguin"));
        await repository.Add(BuildBook(title: "B", publisher: "Alexandria Press"));
        await repository.Add(BuildBook(title: "C", publisher: "Alexandria Press"));
        await repository.Add(BuildBook(title: "D", publisher: null));

        var publishers = await repository.GetPublishers();

        Assert.HasCount(2, publishers);
        Assert.AreEqual("Alexandria Press", publishers[0]);
        Assert.AreEqual("Penguin", publishers[1]);
    }
}
