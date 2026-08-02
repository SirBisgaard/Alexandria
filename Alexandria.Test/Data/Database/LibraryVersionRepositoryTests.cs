using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.DataAccess.Database.Repositories;

namespace Alexandria.Tests.Data.Database;

[TestClass]
public sealed class LibraryVersionRepositoryTests : DatabaseTestBase
{
    private LibraryVersionRepository repository = null!;
    private BookRepository bookRepository = null!;

    [TestInitialize]
    public void SetupRepo()
    {
        repository = new LibraryVersionRepository(ConnectionProvider);
        bookRepository = new BookRepository(ConnectionProvider, new AuthorRepository(ConnectionProvider), new SeriesRepository(ConnectionProvider));
    }

    [TestMethod]
    public async Task GetVersionToken_NoChangesBetweenCalls_ReturnsSameToken()
    {
        var first = await repository.GetVersionToken();
        var second = await repository.GetVersionToken();

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task GetVersionToken_AfterBookAdded_Changes()
    {
        var before = await repository.GetVersionToken();

        await bookRepository.Add(BuildBook());

        Assert.AreNotEqual(before, await repository.GetVersionToken());
    }

    [TestMethod]
    public async Task GetVersionToken_AfterBookUpdated_Changes()
    {
        var book = BuildBook();
        await bookRepository.Add(book);
        var before = await repository.GetVersionToken();

        book.ModifiedAt = book.ModifiedAt.AddSeconds(1);
        await bookRepository.Update(book);

        Assert.AreNotEqual(before, await repository.GetVersionToken());
    }

    [TestMethod]
    public async Task GetVersionToken_AfterBookRemoved_Changes()
    {
        var kept = BuildBook(title: "Kept");
        var removed = BuildBook(title: "Removed");
        await bookRepository.Add(kept);
        await bookRepository.Add(removed);
        var before = await repository.GetVersionToken();

        await bookRepository.Remove(removed.BookUuid);

        Assert.AreNotEqual(before, await repository.GetVersionToken());
    }

    [TestMethod]
    public async Task GetVersionToken_AfterReadingStateUpsert_Changes()
    {
        var bookUuid = Guid.NewGuid();
        await InsertBookShell(bookUuid);
        var readingStateRepository = new ReadingStateRepository(ConnectionProvider);
        var before = await repository.GetVersionToken();

        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        await readingStateRepository.Upsert(bookUuid, new KoboStoreReadingState
        {
            EntitlementId = bookUuid.ToString(),
            Created = now,
            LastModified = now,
            PriorityTimestamp = now,
            StatusInfo = new KoboStoreStatusInfo { LastModified = now, Status = "Reading" },
            Statistics = new KoboStoreStatistics { LastModified = now },
            CurrentBookmark = new KoboStoreBookmark { LastModified = now }
        });

        Assert.AreNotEqual(before, await repository.GetVersionToken());
    }
}
