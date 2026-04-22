using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Files.Books.Audiobooks;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class M4BFileRenamerTests
{
    private string tempDir = null!;
    private M4BFileRenamer renamer = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        renamer = new M4BFileRenamer();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public void PlanRename_PreservesM4bExtension()
    {
        var source = Path.Combine(tempDir, "original.m4b");
        var book = BuildAudiobook(source);

        var step = renamer.PlanRename(book, "Author - Title");

        Assert.AreEqual(Path.Combine(tempDir, "Author - Title.m4b"), step.NewSourcePath);
        Assert.IsEmpty(step.ChapterRenames);
    }

    [TestMethod]
    public void Execute_MovesFileOnDisk()
    {
        var source = Fixtures.CopyM4BTo(tempDir);
        var book = BuildAudiobook(source);
        var step = renamer.PlanRename(book, "Renamed");

        renamer.Execute(step);

        Assert.IsFalse(File.Exists(source));
        Assert.IsTrue(File.Exists(step.NewSourcePath));
    }

    private static Book BuildAudiobook(string sourcePath)
    {
        return new Book
        {
            Title = "Title",
            Authors = [new Author { Name = "Author" }],
            File = new BookFile { SourcePath = sourcePath, MediaType = MediaType.Audiobook }
        };
    }
}
