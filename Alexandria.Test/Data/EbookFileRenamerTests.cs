using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Files.Books.Ebooks;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class EbookFileRenamerTests
{
    private string tempDir = null!;
    private EbookFileRenamer renamer = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        renamer = new EbookFileRenamer();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public void PlanRename_PreservesExtensionAndDirectory()
    {
        var source = Path.Combine(tempDir, "original.epub");
        var book = BuildEbook(source);

        var step = renamer.PlanRename(book, "Author - Title");

        Assert.AreEqual(Path.Combine(tempDir, "Author - Title.epub"), step.NewSourcePath);
        Assert.AreEqual(source, step.OldSourcePath);
        Assert.IsEmpty(step.ChapterRenames);
    }

    [TestMethod]
    public void Execute_MovesFileOnDisk()
    {
        var source = Fixtures.CopyEpubTo(tempDir);
        var book = BuildEbook(source);
        var step = renamer.PlanRename(book, "Renamed");

        renamer.Execute(step);

        Assert.IsFalse(File.Exists(source));
        Assert.IsTrue(File.Exists(step.NewSourcePath));
    }

    [TestMethod]
    public void Execute_NoOp_WhenSameBaseName()
    {
        var source = Fixtures.CopyEpubTo(tempDir);
        var book = BuildEbook(source);
        var baseName = Path.GetFileNameWithoutExtension(source);

        var step = renamer.PlanRename(book, baseName);
        renamer.Execute(step);

        Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public void Execute_InvertUndoesForward()
    {
        var source = Fixtures.CopyEpubTo(tempDir);
        var book = BuildEbook(source);
        var step = renamer.PlanRename(book, "Renamed");

        renamer.Execute(step);
        Assert.IsTrue(File.Exists(step.NewSourcePath));

        renamer.Execute(step.Invert());
        Assert.IsTrue(File.Exists(source));
        Assert.IsFalse(File.Exists(step.NewSourcePath));
    }

    private static Book BuildEbook(string sourcePath)
    {
        return new Book
        {
            Title = "Title",
            Authors = [new Author { Name = "Author" }],
            File = new BookFile { SourcePath = sourcePath, MediaType = MediaType.Ebook }
        };
    }
}
