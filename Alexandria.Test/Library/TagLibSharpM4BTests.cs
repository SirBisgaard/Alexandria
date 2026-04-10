namespace Alexandria.Tests.Library;

[TestClass]
[TestCategory("LargeFile")]
public sealed class TagLibSharpM4BTests
{
    private static readonly string ContentAudiobooksDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Audiobooks");
    private static readonly string LargeFileName = "Christopher Glyn - The Complete King James Version Audio Bible.m4b";

    private string outputDir = null!;

    [TestInitialize]
    public void Setup()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "alexandria-taglib-test-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        RequireLargeFile();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);
    }

    private static string LargeFilePath => Path.Combine(ContentAudiobooksDir, LargeFileName);

    private static void RequireLargeFile()
    {
        if (!File.Exists(LargeFilePath))
            Assert.Inconclusive("Large audiobook test file not found — skipping.");
    }

    private string CopyToOutputDir()
    {
        var dest = Path.Combine(outputDir, LargeFileName);
        File.Copy(LargeFilePath, dest);
        return dest;
    }

    // --- Read tests ---

    [TestMethod]
    public void ReadLargeFile_OpensWithoutError()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotNull(file);
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsCorrectTitle()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.AreEqual("The Complete King James Version Audio Bible", file.Tag.Title);
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsCorrectArtist()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotNull(file.Tag.FirstPerformer);
        Assert.AreEqual("Christopher Glyn", file.Tag.FirstPerformer);
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsCorrectAlbumArtist()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotNull(file.Tag.FirstAlbumArtist);
        Assert.AreEqual("Christopher Glyn", file.Tag.FirstAlbumArtist);
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsCorrectComposer()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotEmpty(file.Tag.Composers);
        Assert.AreEqual("Christopher Glyn", file.Tag.Composers[0]);
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsCorrectGenre()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotNull(file.Tag.FirstGenre);
        Assert.AreEqual("Bibles", file.Tag.FirstGenre);
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsCorrectYear()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.AreEqual(2018u, file.Tag.Year);
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsCorrectComment()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotNull(file.Tag.Comment);
        Assert.StartsWith("Since its first publication in 1611", file.Tag.Comment);
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsCorrectCopyright()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotNull(file.Tag.Copyright);
        Assert.IsTrue(file.Tag.Copyright.Contains("Public Domain"), $"Expected copyright containing 'Public Domain', got '{file.Tag.Copyright}'");
    }

    [TestMethod]
    public void ReadLargeFile_HasEmbeddedCover()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotEmpty(file.Tag.Pictures);
        Assert.IsGreaterThan(0, file.Tag.Pictures[0].Data.Count);
    }

    [TestMethod]
    public void ReadLargeFile_CoverIsJpeg()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsNotEmpty(file.Tag.Pictures);
        var data = file.Tag.Pictures[0].Data.Data;
        Assert.AreEqual(0xFF, data[0], "First byte should be JPEG SOI marker.");
        Assert.AreEqual(0xD8, data[1], "Second byte should be JPEG SOI marker.");
    }

    [TestMethod]
    public void ReadLargeFile_ReturnsPositiveDuration()
    {
        using var file = TagLib.File.Create(LargeFilePath);
        Assert.IsGreaterThan(0.0, file.Properties.Duration.TotalSeconds);
    }

    // --- Write roundtrip tests ---

    [TestMethod]
    public void WriteMetadata_TitleRoundtrip()
    {
        var copy = CopyToOutputDir();

        using (var file = TagLib.File.Create(copy))
        {
            file.Tag.Title = "New Test Title";
            file.Save();
        }

        using (var file = TagLib.File.Create(copy))
        {
            Assert.AreEqual("New Test Title", file.Tag.Title);
        }
    }

    [TestMethod]
    public void WriteMetadata_ArtistRoundtrip()
    {
        var copy = CopyToOutputDir();

        using (var file = TagLib.File.Create(copy))
        {
            file.Tag.Performers = ["Test Author"];
            file.Save();
        }

        using (var file = TagLib.File.Create(copy))
        {
            Assert.AreEqual("Test Author", file.Tag.FirstPerformer);
            Assert.AreEqual("Bibles", file.Tag.FirstGenre, "Genre should be preserved after artist change.");
            Assert.IsNotNull(file.Tag.Comment, "Comment should be preserved after artist change.");
        }
    }

    [TestMethod]
    public void WriteMetadata_PreservesCover()
    {
        var copy = CopyToOutputDir();

        byte[] originalCoverData;
        using (var file = TagLib.File.Create(copy))
        {
            Assert.IsNotEmpty(file.Tag.Pictures, "File should have a cover before the test.");
            originalCoverData = file.Tag.Pictures[0].Data.Data;
        }

        using (var file = TagLib.File.Create(copy))
        {
            file.Tag.Title = "Modified Title";
            file.Save();
        }

        using (var file = TagLib.File.Create(copy))
        {
            Assert.IsNotEmpty(file.Tag.Pictures, "Cover should still be present after metadata write.");
            var reReadCover = file.Tag.Pictures[0].Data.Data;
            Assert.AreEqual(originalCoverData.Length, reReadCover.Length, "Cover data length should be unchanged.");
            Assert.AreEqual(0xFF, reReadCover[0], "Cover should still be JPEG.");
            Assert.AreEqual(0xD8, reReadCover[1], "Cover should still be JPEG.");
        }
    }

    [TestMethod]
    public void WriteCover_PreservesMetadata()
    {
        var copy = CopyToOutputDir();

        string originalTitle;
        string originalArtist;
        string originalComment;
        using (var file = TagLib.File.Create(copy))
        {
            originalTitle = file.Tag.Title;
            originalArtist = file.Tag.FirstPerformer;
            originalComment = file.Tag.Comment;
        }

        var fakeCover = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        using (var file = TagLib.File.Create(copy))
        {
            file.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(fakeCover)) { Type = TagLib.PictureType.FrontCover, MimeType = "image/jpeg" }];
            file.Save();
        }

        using (var file = TagLib.File.Create(copy))
        {
            Assert.AreEqual(originalTitle, file.Tag.Title, "Title should be preserved after cover write.");
            Assert.AreEqual(originalArtist, file.Tag.FirstPerformer, "Artist should be preserved after cover write.");
            Assert.AreEqual(originalComment, file.Tag.Comment, "Comment should be preserved after cover write.");
        }
    }

    [TestMethod]
    public void RemoveCover_PreservesMetadata()
    {
        var copy = CopyToOutputDir();

        string originalTitle;
        string originalArtist;
        using (var file = TagLib.File.Create(copy))
        {
            originalTitle = file.Tag.Title;
            originalArtist = file.Tag.FirstPerformer;
        }

        using (var file = TagLib.File.Create(copy))
        {
            file.Tag.Pictures = [];
            file.Save();
        }

        using (var file = TagLib.File.Create(copy))
        {
            Assert.IsEmpty(file.Tag.Pictures, "Cover should be removed.");
            Assert.AreEqual(originalTitle, file.Tag.Title, "Title should be preserved after cover removal.");
            Assert.AreEqual(originalArtist, file.Tag.FirstPerformer, "Artist should be preserved after cover removal.");
        }
    }
}
