using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Files.Books.Audiobooks;
using Alexandria.Tests.TestData;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class Mp3FolderFileRenamerTests
{
    private string tempDir = null!;
    private Mp3FolderFileRenamer renamer = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        renamer = new Mp3FolderFileRenamer();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public void PlanRename_ComputesCanonicalChapterNames()
    {
        var folderName = "old-folder";
        var physical = Fixtures.CopyMp3AudiobookTo(tempDir, folderName);
        var book = BuildAudiobook(physical + ".mp3audiobook", "The Great Title", chapterFiles: ["1.mp3", "2.mp3", "3.mp3", "4.mp3"]);

        var step = renamer.PlanRename(book, "Author - Title");

        Assert.AreEqual(4, step.ChapterRenames.Count);
        Assert.AreEqual("1.mp3", step.ChapterRenames[0].OldFileName);
        Assert.AreEqual("01 - The Great Title.mp3", step.ChapterRenames[0].NewFileName);
        Assert.AreEqual("04 - The Great Title.mp3", step.ChapterRenames[3].NewFileName);
        Assert.AreEqual(Path.Combine(tempDir, "Author - Title.mp3audiobook"), step.NewSourcePath);
    }

    [TestMethod]
    public void Execute_RenamesFolderAndChapterFiles()
    {
        var folderName = "old-folder";
        var physical = Fixtures.CopyMp3AudiobookTo(tempDir, folderName);
        var book = BuildAudiobook(physical + ".mp3audiobook", "Title", chapterFiles: ["1.mp3", "2.mp3", "3.mp3", "4.mp3"]);

        var step = renamer.PlanRename(book, "Renamed");
        renamer.Execute(step);

        var newFolder = Path.Combine(tempDir, "Renamed");
        Assert.IsFalse(Directory.Exists(physical), "Original folder should be gone.");
        Assert.IsTrue(Directory.Exists(newFolder), "New folder should exist.");
        Assert.IsTrue(File.Exists(Path.Combine(newFolder, "01 - Title.mp3")));
        Assert.IsTrue(File.Exists(Path.Combine(newFolder, "02 - Title.mp3")));
        Assert.IsTrue(File.Exists(Path.Combine(newFolder, "03 - Title.mp3")));
        Assert.IsTrue(File.Exists(Path.Combine(newFolder, "04 - Title.mp3")));
        Assert.IsFalse(File.Exists(Path.Combine(newFolder, "1.mp3")));
    }

    [TestMethod]
    public void Execute_Invert_RestoresOriginalFolderAndChapterNames()
    {
        var folderName = "old-folder";
        var physical = Fixtures.CopyMp3AudiobookTo(tempDir, folderName);
        var book = BuildAudiobook(physical + ".mp3audiobook", "Title", chapterFiles: ["1.mp3", "2.mp3", "3.mp3", "4.mp3"]);
        var step = renamer.PlanRename(book, "Renamed");

        renamer.Execute(step);
        renamer.Execute(step.Invert());

        Assert.IsTrue(Directory.Exists(physical));
        Assert.IsFalse(Directory.Exists(Path.Combine(tempDir, "Renamed")));
        Assert.IsTrue(File.Exists(Path.Combine(physical, "1.mp3")));
        Assert.IsTrue(File.Exists(Path.Combine(physical, "4.mp3")));
    }

    [TestMethod]
    public void Execute_ChapterRenameFailure_RollsBackFolderAndCompletedChapters()
    {
        var folderName = "old-folder";
        var physical = Fixtures.CopyMp3AudiobookTo(tempDir, folderName);
        var book = BuildAudiobook(physical + ".mp3audiobook", "Title", chapterFiles: ["1.mp3", "missing.mp3", "3.mp3", "4.mp3"]);

        var step = renamer.PlanRename(book, "Renamed");

        Assert.ThrowsExactly<FileNotFoundException>(() => renamer.Execute(step));

        Assert.IsTrue(Directory.Exists(physical), "Original folder should be restored on partial failure.");
        Assert.IsTrue(File.Exists(Path.Combine(physical, "1.mp3")), "Completed chapter rename should be rolled back.");
    }

    [TestMethod]
    public void PlanRename_WidthScalesWithChapterCount()
    {
        var book = new Book
        {
            Title = "Title",
            Authors = [new Author { Name = "A" }],
            File = new BookFile { SourcePath = Path.Combine(tempDir, "x.mp3audiobook"), MediaType = MediaType.Audiobook },
            Chapters = Enumerable.Range(1, 12).Select(i => new AudiobookChapter { ChapterIndex = i - 1, FileName = $"{i}.mp3" }).ToList()
        };

        var step = renamer.PlanRename(book, "renamed");

        Assert.AreEqual("01 - Title.mp3", step.ChapterRenames[0].NewFileName);
        Assert.AreEqual("12 - Title.mp3", step.ChapterRenames[11].NewFileName);
    }

    private static Book BuildAudiobook(string sourcePath, string title, string[] chapterFiles)
    {
        return new Book
        {
            Title = title,
            Authors = [new Author { Name = "Author" }],
            File = new BookFile { SourcePath = sourcePath, MediaType = MediaType.Audiobook },
            Chapters = chapterFiles.Select((f, i) => new AudiobookChapter { ChapterIndex = i, FileName = f }).ToList()
        };
    }
}
