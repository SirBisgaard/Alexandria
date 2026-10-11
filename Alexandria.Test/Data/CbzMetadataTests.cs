using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Books.Comics;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class CbzMetadataTests
{
    private const string FullComicInfo = """
        <Title>The Long Road</Title><Series>Road Saga</Series><Number>2,5</Number><Summary>A journey.</Summary>
        <Year>2013</Year><Month>11</Month><Day>5</Day><Writer>Ann Writer, Bob Writer</Writer><Penciller>Pat Pencil</Penciller>
        <CoverArtist>Cora Cover</CoverArtist><Publisher>Test Comics</Publisher><Genre>Fantasy, Adventure</Genre>
        <LanguageISO>da</LanguageISO><Manga>YesAndRightToLeft</Manga><GTIN>9781234567897</GTIN><StoryArc>Keep Me</StoryArc>
        """;

    private string tempDir = null!;
    private CbzFileHandler handler = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        handler = new CbzFileHandler();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public void CbzMetadata_ExtractWithComicInfo_ReadsAllFields()
    {
        var path = ComicFixtures.CreateSimpleCbz(tempDir, "comic.cbz", FullComicInfo);

        var (book, cover) = handler.Extract(path);

        Assert.AreEqual("The Long Road", book.Title);
        CollectionAssert.AreEqual(new[] { "Ann Writer", "Bob Writer" }, book.Authors.Select(a => a.Name).ToArray());
        Assert.AreEqual("Road Saga", book.Series?.Name);
        Assert.AreEqual(2.5m, book.Series?.Index);
        Assert.AreEqual("A journey.", book.Description);
        Assert.AreEqual("2013-11-05", book.PublicationDate);
        Assert.AreEqual("Test Comics", book.Publisher);
        Assert.AreEqual("da", book.Language);
        Assert.AreEqual("9781234567897", book.Isbn);
        CollectionAssert.AreEqual(new[] { "Fantasy", "Adventure" }, book.Subjects);
        Assert.IsTrue(book.Contributors.Any(c => c is { Name: "Pat Pencil", Role: "Penciller" }));
        Assert.IsTrue(book.Contributors.Any(c => c is { Name: "Cora Cover", Role: "Cover Artist" }));
        Assert.AreEqual(MediaType.Comic, book.File.MediaType);
        Assert.AreEqual(ComicReadingDirection.RightToLeft, book.File.ReadingDirection);
        Assert.AreEqual(ComicColorMode.Auto, book.File.ColorMode);
        Assert.AreEqual(3, book.File.PageCount);
        CollectionAssert.AreEqual(ComicFixtures.GrayPage(1), cover);
    }

    [TestMethod]
    public void CbzMetadata_ExtractWithoutComicInfo_ParsesRenameTemplateFileName()
    {
        var path = ComicFixtures.CreateSimpleCbz(tempDir, "Ann Writer - Road Saga #3 - The Last Road.cbz");

        var book = handler.ExtractMetadata(path);

        Assert.AreEqual("The Last Road", book.Title);
        Assert.AreEqual("Ann Writer", book.Authors.Single().Name);
        Assert.AreEqual("Road Saga", book.Series?.Name);
        Assert.AreEqual(3m, book.Series?.Index);
        Assert.AreEqual(ComicReadingDirection.LeftToRight, book.File.ReadingDirection);
    }

    [TestMethod]
    public void CbzMetadata_ExtractWithoutComicInfo_SeriesNumberFileNameBecomesSeries()
    {
        var path = ComicFixtures.CreateSimpleCbz(tempDir, "World Chronicle #2.cbz");

        var book = handler.ExtractMetadata(path);

        Assert.AreEqual("World Chronicle #2", book.Title);
        Assert.IsEmpty(book.Authors);
        Assert.AreEqual("World Chronicle", book.Series?.Name);
        Assert.AreEqual(2m, book.Series?.Index);
    }

    [TestMethod]
    public void CbzMetadata_PageOrder_IsNaturalAndIgnoresJunkAndBackslashFolders()
    {
        var path = ComicFixtures.CreateCbz(tempDir, "order.cbz",
        [
            ("Issue 1\\10.jpg", ComicFixtures.GrayPage(10)),
            ("Issue 1\\2.jpg", ComicFixtures.GrayPage(2)),
            ("__MACOSX/Issue 1/._1.jpg", ComicFixtures.GrayPage(99)),
            ("Issue 1\\.hidden.jpg", ComicFixtures.GrayPage(98)),
            ("Issue 1\\notes.txt", Encoding.UTF8.GetBytes("not a page")),
            ("Issue 1\\1.jpg", ComicFixtures.GrayPage(1))
        ]);

        using var zip = ZipFile.OpenRead(path);
        var pages = CbzFileHandler.GetPageEntries(zip).Select(e => e.FullName).ToList();

        CollectionAssert.AreEqual(new[] { "Issue 1\\1.jpg", "Issue 1\\2.jpg", "Issue 1\\10.jpg" }, pages);
    }

    [TestMethod]
    public void CbzMetadata_FrontCoverInPagesBlock_IsUsedAsCover()
    {
        var path = ComicFixtures.CreateSimpleCbz(tempDir, "cover.cbz", """<Title>T</Title><Pages><Page Image="0"/><Page Image="1" Type="FrontCover"/></Pages>""");

        var (_, cover) = handler.Extract(path);

        CollectionAssert.AreEqual(ComicFixtures.GrayPage(2), cover);
    }

    [TestMethod]
    public void CbzMetadata_EmbedRoundTrip_KeepsPagesAndUnmanagedElements()
    {
        var path = ComicFixtures.CreateSimpleCbz(tempDir, "edit.cbz", FullComicInfo);
        var book = handler.ExtractMetadata(path);
        book.Title = "Renamed";
        book.Authors = [new Author { Name = "New Writer" }];
        book.Series = new Series { Name = "Other Saga", Index = 7 };
        book.Contributors = [new Contributor { Name = "Ink Person", Role = "Inker" }];
        book.PublicationDate = "2020-02";

        handler.EmbedMetadata(path, book);
        var reread = handler.ExtractMetadata(path);

        Assert.AreEqual("Renamed", reread.Title);
        Assert.AreEqual("New Writer", reread.Authors.Single().Name);
        Assert.AreEqual("Other Saga", reread.Series?.Name);
        Assert.AreEqual(7m, reread.Series?.Index);
        Assert.AreEqual("2020-02", reread.PublicationDate);
        Assert.AreEqual("Ink Person", reread.Contributors.Single().Name);
        Assert.AreEqual(ComicReadingDirection.RightToLeft, reread.File.ReadingDirection);
        Assert.AreEqual(3, reread.File.PageCount);

        var comicInfo = ReadComicInfo(path);
        Assert.AreEqual("Keep Me", comicInfo.Root!.Element("StoryArc")?.Value);
        Assert.IsNull(comicInfo.Root.Element("Penciller"));
        Assert.IsNull(comicInfo.Root.Element("Day"));
        var order = comicInfo.Root.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.IsLessThan(order.IndexOf("StoryArc"), order.IndexOf("Manga"), "elements must follow the ComicInfo schema order");
        Assert.IsLessThan(order.IndexOf("Inker"), order.IndexOf("Writer"));
    }

    [TestMethod]
    public void CbzMetadata_EmbedIntoArchiveWithoutComicInfo_AddsOne()
    {
        var path = ComicFixtures.CreateSimpleCbz(tempDir, "Ann - Plain.cbz");
        var book = handler.ExtractMetadata(path);
        book.File.ReadingDirection = ComicReadingDirection.RightToLeft;

        handler.EmbedMetadata(path, book);

        Assert.AreEqual("YesAndRightToLeft", ReadComicInfo(path).Root!.Element("Manga")?.Value);
        Assert.AreEqual(ComicReadingDirection.RightToLeft, handler.ExtractMetadata(path).File.ReadingDirection);
    }

    [TestMethod]
    public void CbzMetadata_EmbedLeftToRight_DowngradesRightToLeftMangaFlag()
    {
        var path = ComicFixtures.CreateSimpleCbz(tempDir, "manga.cbz", "<Title>M</Title><Manga>YesAndRightToLeft</Manga>");
        var book = handler.ExtractMetadata(path);
        book.File.ReadingDirection = ComicReadingDirection.LeftToRight;

        handler.EmbedMetadata(path, book);

        Assert.AreEqual("Yes", ReadComicInfo(path).Root!.Element("Manga")?.Value);
        Assert.AreEqual(ComicReadingDirection.LeftToRight, handler.ExtractMetadata(path).File.ReadingDirection);
    }

    [TestMethod]
    public void CbzMetadata_MalformedComicInfo_FallsBackToFileName()
    {
        var path = ComicFixtures.CreateCbz(tempDir, "Ann - Broken.cbz", [("1.jpg", ComicFixtures.GrayPage(1)), ("ComicInfo.xml", "<ComicInfo><Title>"u8.ToArray())]);

        var book = handler.ExtractMetadata(path);

        Assert.AreEqual("Broken", book.Title);
    }

    [TestMethod]
    public void NaturalStringComparer_OrdersNumbersByValue()
    {
        string[] names = ["v1-010.jpg", "v1-2.jpg", "V1-001.jpg", "v1-001x1.jpg", "cover.jpg"];

        var sorted = names.Order(NaturalStringComparer.Instance).ToArray();

        CollectionAssert.AreEqual(new[] { "cover.jpg", "V1-001.jpg", "v1-001x1.jpg", "v1-2.jpg", "v1-010.jpg" }, sorted);
    }

    private static XDocument ReadComicInfo(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("ComicInfo.xml")!.Open();
        return XDocument.Load(stream);
    }
}
