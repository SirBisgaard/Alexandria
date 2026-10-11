using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Books.Comics;
using Alexandria.Tests.TestData;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class ComicEpubBuilderTests
{
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly ComicDeviceProfile GrayProfile = new() { Width = 1072, Height = 1448 };
    private static readonly ComicDeviceProfile ColorProfile = new() { Width = 1264, Height = 1680, IsColor = true };

    private string tempDir = null!;
    private ComicEpubBuilder builder = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        builder = new ComicEpubBuilder(NullLogger<ComicEpubBuilder>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public void ComicEpub_Build_WritesFixedLayoutPackage()
    {
        var book = LoadBook(ComicFixtures.CreateSimpleCbz(tempDir, "Ann - Saga #1 - Pages.cbz", "<Title>Pages</Title><Writer>Ann</Writer><Series>Saga</Series><Number>1</Number><LanguageISO>en</LanguageISO>"));

        var epub = Build(book, GrayProfile);

        using var zip = ZipFile.OpenRead(epub);
        var first = zip.Entries[0];
        Assert.AreEqual("mimetype", first.FullName);
        Assert.AreEqual(first.Length, first.CompressedLength, "mimetype must be stored uncompressed");
        Assert.IsNotNull(zip.GetEntry("META-INF/container.xml"));

        var opf = ReadOpf(zip);
        var metas = opf.Descendants(Opf + "meta").ToList();
        Assert.AreEqual("pre-paginated", metas.Single(m => (string?)m.Attribute("property") == "rendition:layout").Value);
        Assert.AreEqual("landscape", metas.Single(m => (string?)m.Attribute("property") == "rendition:spread").Value);
        Assert.AreEqual("Saga", metas.Single(m => (string?)m.Attribute("property") == "belongs-to-collection").Value);
        Assert.AreEqual($"urn:uuid:{book.BookUuid}", opf.Descendants().Single(e => e.Name.LocalName == "identifier").Value);
        Assert.AreEqual("Pages", opf.Descendants().Single(e => e.Name.LocalName == "title").Value);

        var spine = opf.Descendants(Opf + "spine").Single();
        Assert.AreEqual("ltr", (string?)spine.Attribute("page-progression-direction"));
        Assert.HasCount(3, spine.Elements());
        Assert.AreEqual("cover-image", (string?)opf.Descendants(Opf + "item").Single(i => (string?)i.Attribute("id") == "cover").Attribute("properties"));

        var page = ReadText(zip, "OEBPS/Text/p0001.xhtml");
        StringAssert.Contains(page, "<meta name=\"viewport\" content=\"width=601, height=900\"/>");
        StringAssert.Contains(page, "src=\"../Images/p0001.jpg\" width=\"601\" height=\"900\"");
    }

    [TestMethod]
    public void ComicEpub_Build_KeepsNaturalPageOrder()
    {
        var path = ComicFixtures.CreateCbz(tempDir, "order.cbz", [("10.jpg", ComicFixtures.GrayPage(10)), ("2.jpg", ComicFixtures.GrayPage(2)), ("1.jpg", ComicFixtures.GrayPage(1))]);

        var epub = Build(LoadBook(path), GrayProfile);

        CollectionAssert.AreEqual(new[] { 601, 602, 610 }, ImageWidths(epub));
    }

    [TestMethod]
    public void ComicEpub_Build_RotatesSpreadSoRightEdgePointsUp()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "spread.cbz", [("1.jpg", ComicFixtures.GrayPage(1)), ("2.jpg", ComicFixtures.SpreadPage()), ("3.jpg", ComicFixtures.GrayPage(3))]));
        book.File.ColorMode = ComicColorMode.Color;

        var epub = Build(book, ColorProfile);

        using var zip = ZipFile.OpenRead(epub);
        Assert.AreEqual(3, zip.Entries.Count(e => e.FullName.StartsWith("OEBPS/Images/")));
        using var spread = ReadImage(zip, "OEBPS/Images/p0002.jpg");
        Assert.AreEqual(1000u, spread.Width);
        Assert.AreEqual(1600u, spread.Height);
        Assert.IsFalse(IsRedAt(spread, 0.25), "the spread's right (blue) half must end up on top");
        Assert.IsTrue(IsRedAt(spread, 0.75), "the spread's left (red) half must end up at the bottom");
        CollectionAssert.AreEqual(new[] { "left", "center", "left" }, SpreadSides(ReadOpf(zip)));
    }

    [TestMethod]
    public void ComicEpub_Build_RightToLeftFlipsSpineAndRotatesTheSameWay()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "manga.cbz", [("1.jpg", ComicFixtures.GrayPage(1)), ("2.jpg", ComicFixtures.SpreadPage()), ("3.jpg", ComicFixtures.GrayPage(3)), ("4.jpg", ComicFixtures.GrayPage(4))]));
        book.File.ReadingDirection = ComicReadingDirection.RightToLeft;
        book.File.ColorMode = ComicColorMode.Color;

        var epub = Build(book, ColorProfile);

        using var zip = ZipFile.OpenRead(epub);
        using var spread = ReadImage(zip, "OEBPS/Images/p0002.jpg");
        Assert.IsFalse(IsRedAt(spread, 0.25));
        var opf = ReadOpf(zip);
        Assert.AreEqual("rtl", (string?)opf.Descendants(Opf + "spine").Single().Attribute("page-progression-direction"));
        CollectionAssert.AreEqual(new[] { "right", "center", "right", "left" }, SpreadSides(opf));
    }

    [TestMethod]
    public void ComicEpub_Build_SplitModeCutsSpreadIntoPagesInReadingOrder()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "split.cbz", [("1.jpg", ComicFixtures.GrayPage(1)), ("2.jpg", ComicFixtures.SpreadPage()), ("3.jpg", ComicFixtures.GrayPage(3))]));
        book.File.ColorMode = ComicColorMode.Color;

        var epub = Build(book, ColorProfile, new ComicRenderingSettings { SpreadHandling = ComicSpreadHandling.Split });

        using var zip = ZipFile.OpenRead(epub);
        Assert.AreEqual(4, zip.Entries.Count(e => e.FullName.StartsWith("OEBPS/Images/")));
        using var first = ReadImage(zip, "OEBPS/Images/p0002.jpg");
        using var second = ReadImage(zip, "OEBPS/Images/p0003.jpg");
        Assert.IsTrue(IsRedAt(first, 0.5), "the left (red) half comes first in left-to-right reading");
        Assert.IsFalse(IsRedAt(second, 0.5));
        CollectionAssert.AreEqual(new[] { "left", "left", "right", "left" }, SpreadSides(ReadOpf(zip)), "split halves are pinned left/right so they reunite in landscape");
    }

    [TestMethod]
    public void ComicEpub_Build_SplitModeRightToLeftPutsRightHalfFirst()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "split-rtl.cbz", [("1.jpg", ComicFixtures.SpreadPage())]));
        book.File.ReadingDirection = ComicReadingDirection.RightToLeft;
        book.File.ColorMode = ComicColorMode.Color;

        var epub = Build(book, ColorProfile, new ComicRenderingSettings { SpreadHandling = ComicSpreadHandling.Split });

        using var zip = ZipFile.OpenRead(epub);
        using var first = ReadImage(zip, "OEBPS/Images/p0001.jpg");
        Assert.IsFalse(IsRedAt(first, 0.5), "the right (blue) half comes first in right-to-left reading");
        CollectionAssert.AreEqual(new[] { "right", "left" }, SpreadSides(ReadOpf(zip)));
    }

    [TestMethod]
    public void ComicEpub_Build_SplitModeRotatesPanoramasInstead()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "panorama.cbz", [("1.jpg", ComicFixtures.SpreadPage(2000, 1000))]));

        var epub = Build(book, ColorProfile, new ComicRenderingSettings { SpreadHandling = ComicSpreadHandling.Split });

        using var zip = ZipFile.OpenRead(epub);
        using var only = ReadImage(zip, "OEBPS/Images/p0001.jpg");
        Assert.AreEqual(1, zip.Entries.Count(e => e.FullName.StartsWith("OEBPS/Images/")));
        Assert.IsGreaterThan(only.Width, only.Height, "a panorama is rotated to portrait rather than split into two still-landscape halves");
    }

    [TestMethod]
    public void ComicEpub_Build_SplitAndRotateShowsHalvesThenTheRotatedSpread()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "both.cbz", [("1.jpg", ComicFixtures.SpreadPage())]));
        book.File.ColorMode = ComicColorMode.Color;

        var epub = Build(book, ColorProfile, new ComicRenderingSettings { SpreadHandling = ComicSpreadHandling.SplitAndRotate });

        using var zip = ZipFile.OpenRead(epub);
        using var rotated = ReadImage(zip, "OEBPS/Images/p0003.jpg");
        Assert.AreEqual(3, zip.Entries.Count(e => e.FullName.StartsWith("OEBPS/Images/")));
        Assert.IsFalse(IsRedAt(rotated, 0.25), "the rotated copy still has its right edge up");
        CollectionAssert.AreEqual(new[] { "left", "right", "center" }, SpreadSides(ReadOpf(zip)));
    }

    [TestMethod]
    public void ComicEpub_Build_MarginCroppingOffKeepsPageSize()
    {
        var page = ComicFixtures.MarginPage(800, 1200, 200, 240, 600, 960, MagickColors.White, MagickColors.Black);

        var size = SecondPageSize(page, new ComicRenderingSettings { MarginCropping = ComicMarginCropping.Off });

        Assert.AreEqual((800u, 1200u), size);
    }

    [TestMethod]
    public void ComicEpub_Build_AggressiveCroppingIgnoresFaintMarksThatNormalKeeps()
    {
        using var image = new MagickImage(ComicFixtures.MarginPage(800, 1200, 200, 240, 600, 960, MagickColors.White, MagickColors.Black));
        new Drawables().FillColor(new MagickColor("#E0E0E0")).Rectangle(30, 600, 34, 640).Draw(image);
        image.Format = MagickFormat.Png;
        var page = image.ToByteArray();

        var (normalWidth, _) = SecondPageSize(page);
        var (aggressiveWidth, _) = SecondPageSize(page, new ComicRenderingSettings { CroppingStrength = ComicCroppingStrength.Aggressive });

        Assert.IsInRange(692u, 702u, normalWidth, "at Normal a faint mark counts as art, so the left side only crops to it (the right still crops its full 10 %)");
        Assert.AreEqual(640u, aggressiveWidth, "at Aggressive the faint mark is margin and the full 10 % is cropped");
    }

    [TestMethod]
    public void ComicEpub_Build_AutoContrastOffLeavesGrayLevelsAlone()
    {
        var page = ComicFixtures.MarginPage(800, 1200, 0, 0, 400, 1200, new MagickColor("#E6E6E6"), new MagickColor("#282828"));
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "contrast.cbz", [("1.png", page)]));

        var levelled = LightestValue(Build(book, GrayProfile));
        var untouched = LightestValue(Build(book, GrayProfile, new ComicRenderingSettings { AutoContrast = ComicAutoContrast.Off }));

        Assert.IsGreaterThan(245, levelled, "auto-contrast stretches the light gray to white");
        Assert.IsInRange(225, 235, untouched);
    }

    [TestMethod]
    public void ComicEpub_Build_GrayScreenGetsGrayscalePagesEvenForColourArt()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "color.cbz", [("1.jpg", ComicFixtures.ColorPage())]));
        book.File.ColorMode = ComicColorMode.Color;

        var epub = Build(book, GrayProfile);

        CollectionAssert.AreEqual(new[] { ColorSpace.Gray }, ImageColorSpaces(epub));
    }

    [TestMethod]
    public void ComicEpub_Build_AutoKeepsColourOnlyOnColourPages()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "mixed.cbz", [("1.jpg", ComicFixtures.ColorPage()), ("2.jpg", ComicFixtures.GrayPage(2))]));

        var epub = Build(book, ColorProfile);

        CollectionAssert.AreEqual(new[] { ColorSpace.sRGB, ColorSpace.Gray }, ImageColorSpaces(epub));
    }

    [TestMethod]
    public void ComicEpub_Build_ForcedGrayscaleOverridesColourScreen()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "forced.cbz", [("1.jpg", ComicFixtures.ColorPage())]));
        book.File.ColorMode = ComicColorMode.Grayscale;

        var epub = Build(book, ColorProfile);

        CollectionAssert.AreEqual(new[] { ColorSpace.Gray }, ImageColorSpaces(epub));
    }

    [TestMethod]
    public void ComicEpub_Build_ShrinksLargePagesAndNeverEnlargesSmallOnes()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "sizes.cbz", [("1.jpg", ComicFixtures.ColorPage(2000, 3000)), ("2.jpg", ComicFixtures.GrayPage(1, 700))]));

        var epub = Build(book, GrayProfile);

        using var zip = ZipFile.OpenRead(epub);
        using var large = ReadImage(zip, "OEBPS/Images/p0001.jpg");
        using var small = ReadImage(zip, "OEBPS/Images/p0002.jpg");
        Assert.AreEqual(965u, large.Width);
        Assert.AreEqual(1448u, large.Height);
        Assert.AreEqual(601u, small.Width);
        Assert.AreEqual(700u, small.Height);
    }

    [TestMethod]
    public void ComicEpub_Build_CropsMarginsButNeverMoreThanTenPercentPerSide()
    {
        var page = ComicFixtures.MarginPage(800, 1200, 200, 240, 600, 960, MagickColors.White, MagickColors.Black);

        var size = SecondPageSize(page);

        Assert.AreEqual((640u, 960u), size);
    }

    [TestMethod]
    public void ComicEpub_Build_CropsNarrowMarginsToTheArt()
    {
        var page = ComicFixtures.MarginPage(800, 1200, 30, 40, 769, 1159, MagickColors.White, MagickColors.Black);

        var (width, height) = SecondPageSize(page);

        Assert.IsInRange(753u, 759u, width, "art plus 1 % padding per side");
        Assert.IsInRange(1141u, 1147u, height);
    }

    [TestMethod]
    public void ComicEpub_Build_KeepsFaintOrnamentFrameAroundTheArt()
    {
        using var image = new MagickImage(ComicFixtures.MarginPage(800, 1200, 200, 240, 600, 960, MagickColors.White, MagickColors.Black));
        new Drawables().StrokeColor(new MagickColor("#C8C8C8")).StrokeWidth(3).FillColor(MagickColors.Transparent).Rectangle(60, 60, 740, 1140).Draw(image);
        image.Format = MagickFormat.Png;

        var (width, height) = SecondPageSize(image.ToByteArray());

        Assert.IsInRange(696u, 704u, width, "a light-gray ornament frame is art, so cropping must stop outside it");
        Assert.IsInRange(1104u, 1112u, height);
    }

    [TestMethod]
    public void ComicEpub_Build_KeepsDarkOrnamentFrameOnDarkTintedPage()
    {
        using var image = new MagickImage(ComicFixtures.MarginPage(800, 1200, 200, 240, 600, 960, new MagickColor("#6A382F"), new MagickColor("#D8C0A0")));
        new Drawables().StrokeColor(new MagickColor("#8E5A48")).StrokeWidth(3).FillColor(MagickColors.Transparent).Rectangle(60, 60, 740, 1140).Draw(image);
        image.Format = MagickFormat.Png;

        var (width, height) = SecondPageSize(image.ToByteArray());

        Assert.IsInRange(696u, 704u, width, "dark tones must be compared in gamma-encoded luma — in linear light the ornament merges with the background and gets cropped");
        Assert.IsInRange(1104u, 1112u, height);
    }

    [TestMethod]
    public void ComicEpub_Build_KeepsFooterTextCloseToTheEdge()
    {
        using var image = new MagickImage(ComicFixtures.MarginPage(800, 1200, 150, 150, 650, 900, MagickColors.White, MagickColors.Black));
        new Drawables().FillColor(MagickColors.Black).Rectangle(350, 1170, 450, 1180).Draw(image);
        image.Format = MagickFormat.Png;

        var (width, height) = SecondPageSize(image.ToByteArray());

        Assert.AreEqual(640u, width);
        Assert.IsInRange(1070u, 1076u, height, "a footer in the outer 2 % with a gap above it is content, not scan noise");
    }

    [TestMethod]
    public void ComicEpub_Build_EdgeMarkLeavesThatSideUncropped()
    {
        using var image = new MagickImage(ComicFixtures.MarginPage(800, 1200, 100, 150, 700, 1050, MagickColors.White, MagickColors.Black));
        new Drawables().FillColor(MagickColors.Black).Rectangle(2, 0, 6, 1200).Draw(image);
        image.Format = MagickFormat.Png;

        var size = SecondPageSize(image.ToByteArray());

        Assert.AreEqual((720u, 1200u), size, "the full-height edge line counts as content, so only the right side is cropped");
    }

    [TestMethod]
    public void ComicEpub_Build_CropsBlackMargins()
    {
        var page = ComicFixtures.MarginPage(800, 1200, 200, 240, 600, 960, MagickColors.Black, MagickColors.White);

        var size = SecondPageSize(page);

        Assert.AreEqual((640u, 960u), size);
    }

    [TestMethod]
    public void ComicEpub_Build_LeavesColouredCoverUncropped()
    {
        var cover = ComicFixtures.MarginPage(800, 1200, 200, 240, 600, 960, MagickColors.White, MagickColors.Red);
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "cover.cbz", [("1.png", cover), ("2.jpg", ComicFixtures.GrayPage(2))]));

        var epub = Build(book, GrayProfile);

        using var zip = ZipFile.OpenRead(epub);
        using var first = ReadImage(zip, "OEBPS/Images/p0001.jpg");
        Assert.AreEqual((800u, 1200u), (first.Width, first.Height));
    }

    [TestMethod]
    public void ComicEpub_Build_NearlyScreenShapedPageFillsTheScreenExactly()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "fill.cbz", [("1.jpg", ComicFixtures.GrayPage(1)), ("2.jpg", ComicFixtures.ColorPage(1500, 2000)), ("3.jpg", ComicFixtures.ColorPage(2000, 3000))]));

        var epub = Build(book, ColorProfile);

        using var zip = ZipFile.OpenRead(epub);
        using var nearlyScreenShaped = ReadImage(zip, "OEBPS/Images/p0002.jpg");
        using var differentShape = ReadImage(zip, "OEBPS/Images/p0003.jpg");
        Assert.AreEqual((1264u, 1680u), (nearlyScreenShaped.Width, nearlyScreenShaped.Height));
        Assert.AreEqual((1120u, 1680u), (differentShape.Width, differentShape.Height), "pages of a clearly different shape are fitted, not cropped");
    }

    [TestMethod]
    public void ComicEpub_Build_OneNavEntryPerSourceFolder()
    {
        var path = ComicFixtures.CreateCbz(tempDir, "issues.cbz", [("Issue 1/1.jpg", ComicFixtures.GrayPage(1)), ("Issue 1/2.jpg", ComicFixtures.GrayPage(2)), ("Issue 2/1.jpg", ComicFixtures.GrayPage(3))]);

        var epub = Build(LoadBook(path), GrayProfile);

        using var zip = ZipFile.OpenRead(epub);
        var nav = ReadText(zip, "OEBPS/nav.xhtml");
        StringAssert.Contains(nav, "<a href=\"Text/p0001.xhtml\">Issue 1</a>");
        StringAssert.Contains(nav, "<a href=\"Text/p0003.xhtml\">Issue 2</a>");
    }

    [TestMethod]
    public void ComicEpub_Build_ArchiveWithoutPagesThrows()
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, "empty.cbz", [("readme.txt", "no pages"u8.ToArray())]));

        Assert.ThrowsExactly<InvalidDataException>(() => Build(book, GrayProfile));
    }

    private static Book LoadBook(string path)
    {
        var book = new CbzFileHandler().ExtractMetadata(path);
        book.BookUuid = Guid.NewGuid();
        book.File.SourcePath = path;
        return book;
    }

    private string Build(Book book, ComicDeviceProfile profile, ComicRenderingSettings? rendering = null)
    {
        var output = Path.Combine(tempDir, "out", $"{Guid.NewGuid()}.epub");
        builder.Build(book, profile, rendering ?? new ComicRenderingSettings(), output);
        return output;
    }

    private (uint Width, uint Height) SecondPageSize(byte[] page, ComicRenderingSettings? rendering = null)
    {
        var book = LoadBook(ComicFixtures.CreateCbz(tempDir, $"{Guid.NewGuid()}.cbz", [("1.jpg", ComicFixtures.GrayPage(1)), ("2.png", page)]));
        var epub = Build(book, GrayProfile, rendering);
        using var zip = ZipFile.OpenRead(epub);
        using var image = ReadImage(zip, "OEBPS/Images/p0002.jpg");
        return (image.Width, image.Height);
    }

    private static int LightestValue(string epub)
    {
        using var zip = ZipFile.OpenRead(epub);
        using var image = ReadImage(zip, "OEBPS/Images/p0001.jpg");
        return (int)image.Statistics().Composite().Maximum;
    }

    private static XDocument ReadOpf(ZipArchive zip) => XDocument.Parse(ReadText(zip, "OEBPS/content.opf"));

    private static string ReadText(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static List<string> SpreadSides(XDocument opf) => opf.Descendants(Opf + "itemref")
        .Select(i => Regex.Match((string)i.Attribute("properties")!, "page-spread-(\\w+)").Groups[1].Value)
        .ToList();

    private static MagickImage ReadImage(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        return new MagickImage(stream);
    }

    private static List<int> ImageWidths(string epub)
    {
        using var zip = ZipFile.OpenRead(epub);
        return zip.Entries.Where(e => e.FullName.StartsWith("OEBPS/Images/")).OrderBy(e => e.FullName)
            .Select(e => { using var image = ReadImage(zip, e.FullName); return (int)image.Width; })
            .ToList();
    }

    private static List<ColorSpace> ImageColorSpaces(string epub)
    {
        using var zip = ZipFile.OpenRead(epub);
        return zip.Entries.Where(e => e.FullName.StartsWith("OEBPS/Images/")).OrderBy(e => e.FullName)
            .Select(e => { using var image = ReadImage(zip, e.FullName); return image.ColorSpace; })
            .ToList();
    }

    private static bool IsRedAt(MagickImage image, double heightFraction)
    {
        using var pixels = image.GetPixelsUnsafe();
        var color = pixels.GetPixel((int)image.Width / 2, (int)(image.Height * heightFraction)).ToColor()!;
        return color.R > 200 && color.B < 80;
    }
}
