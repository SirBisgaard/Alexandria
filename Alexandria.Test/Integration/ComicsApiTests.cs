using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.Integration;

/// <summary>
/// End-to-end comic flow through the real indexer: CBR files are repacked as CBZ, comics are
/// indexed with a rendition per registered device screen, and the edit endpoint writes reading
/// direction to ComicInfo.xml while the colour mode survives reindexing from the database alone.
/// </summary>
[TestClass]
public sealed class ComicsApiTests : IntegrationTestBase
{
    private static readonly ComicDeviceProfile LibraColour = ComicDeviceProfile.ForModelCode("0390")!;

    [TestMethod]
    public async Task Index_CbrAndCbz_ConvertsAndIndexesComicsWithDeviceRenditions()
    {
        await RegisterLibraColour();
        ComicFixtures.CreateStoredRar(EbookFolder, "Ann - Saga #1 - First.cbr", [("1.jpg", ComicFixtures.GrayPage(1)), ("2.jpg", ComicFixtures.ColorPage())]);
        ComicFixtures.CreateSimpleCbz(EbookFolder, "Ann - Saga #2 - Second.cbz");

        await GetService<IIndexerService>().Index();

        Assert.IsFalse(File.Exists(Path.Combine(EbookFolder, "Ann - Saga #1 - First.cbr")));
        var books = await GetService<IBookRepository>().GetAll();
        Assert.HasCount(2, books);
        var store = GetService<IComicRenditionStore>();
        foreach (var book in books)
        {
            Assert.AreEqual(MediaType.Comic, book.File.MediaType);
            Assert.AreEqual("Saga", book.Series?.Name);
            Assert.IsNotNull(book.File.CoverFilePath);
            Assert.IsNotNull(await store.FindRendition(book, LibraColour), $"'{book.Title}' needs a rendition for the registered Libra Colour");
        }
    }

    [TestMethod]
    public async Task UpdateMetadata_Comic_WritesDirectionToComicInfoAndKeepsColourModeAcrossReindex()
    {
        await RegisterLibraColour();
        var path = ComicFixtures.CreateSimpleCbz(EbookFolder, "Ann - Saga #1 - First.cbz", "<Title>First</Title><Writer>Ann</Writer><Series>Saga</Series><Number>1</Number><Manga>No</Manga>");
        await GetService<IIndexerService>().Index();
        var book = (await GetService<IBookRepository>().GetAll()).Single();
        using var authed = AuthenticatedClient(await LoginAsync());

        using var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/metadata", new
        {
            title = "First",
            authors = new[] { "Ann" },
            language = "en",
            seriesName = "Saga",
            index = 1,
            readingDirection = "RightToLeft",
            colorMode = "Grayscale",
            contributors = new[] { new { name = "Pat", role = "Penciller" } }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var file = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("file");
        Assert.AreEqual("RightToLeft", file.GetProperty("readingDirection").GetString());
        Assert.AreEqual("Grayscale", file.GetProperty("colorMode").GetString());

        using (var zip = ZipFile.OpenRead(path))
        using (var stream = zip.GetEntry("ComicInfo.xml")!.Open())
        {
            var comicInfo = XDocument.Load(stream).Root!;
            Assert.AreEqual("YesAndRightToLeft", comicInfo.Element("Manga")?.Value);
            Assert.AreEqual("Pat", comicInfo.Element("Penciller")?.Value);
        }

        await GetService<IIndexerService>().Index();

        var reindexed = (await GetService<IBookRepository>().GetAll()).Single();
        Assert.AreEqual(ComicColorMode.Grayscale, reindexed.File.ColorMode, "colour mode lives only in the database and must survive a library sync");
        Assert.AreEqual(ComicReadingDirection.RightToLeft, reindexed.File.ReadingDirection);
        Assert.AreEqual(book.EntitlementUuid, reindexed.EntitlementUuid);
        var store = GetService<IComicRenditionStore>();
        Assert.IsNotNull(await store.FindRendition(reindexed, LibraColour));
        Assert.HasCount(1, Directory.GetFiles(Path.Combine(TempDir, "data", "comics", LibraColour.Key)), "the pre-edit rendition must be replaced, not kept alongside");
    }

    [TestMethod]
    public async Task UpdateMetadata_Comic_RejectsNonComicContributorRole()
    {
        ComicFixtures.CreateSimpleCbz(EbookFolder, "Ann - First.cbz");
        await GetService<IIndexerService>().Index();
        var book = (await GetService<IBookRepository>().GetAll()).Single();
        using var authed = AuthenticatedClient(await LoginAsync());

        using var response = await authed.PutAsJsonAsync($"/api/alexandria/books/{book.BookUuid}/metadata", new
        {
            title = "First",
            authors = new[] { "Ann" },
            contributors = new[] { new { name = "Nora", role = "Narrator" } }
        });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateCover_Comic_IsRejected()
    {
        ComicFixtures.CreateSimpleCbz(EbookFolder, "Ann - First.cbz");
        await GetService<IIndexerService>().Index();
        var book = (await GetService<IBookRepository>().GetAll()).Single();
        using var authed = AuthenticatedClient(await LoginAsync());
        using var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(await File.ReadAllBytesAsync(Fixtures.CopyCoverTo(Path.Combine(TempDir, "upload"))));
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(image, "cover", "cover.jpg");

        using var upload = await authed.PutAsync($"/api/alexandria/books/{book.BookUuid}/cover", content);
        using var remove = await authed.DeleteAsync($"/api/alexandria/books/{book.BookUuid}/cover");

        Assert.AreEqual(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, remove.StatusCode);
    }

    [TestMethod]
    public async Task ContributorRoles_Comic_ReturnsComicInfoRoles()
    {
        using var authed = AuthenticatedClient(await LoginAsync());

        var roles = await authed.GetFromJsonAsync<string[]>("/api/alexandria/contributor-roles?mediaType=Comic");

        CollectionAssert.AreEqual(CbzFileHandler.ContributorRoleNames.ToArray(), roles);
    }

    private async Task RegisterLibraColour()
    {
        var devices = GetService<IKoboDeviceRepository>();
        var deviceUuid = Guid.NewGuid();
        await devices.Create(new KoboDevice { DeviceUuid = deviceUuid, Nickname = "Libra" });
        await devices.BindDevice(deviceUuid, "SERIAL", "Kobo Libra Colour", "0390", "4.45.23684");
    }
}
