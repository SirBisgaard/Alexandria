using System.IO.Compression;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Books.Comics;
using Alexandria.Tests.TestData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class CbrComicConverterTests
{
    private string tempDir = null!;
    private string ebookFolder = null!;
    private FileCbrComicConverter converter = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        ebookFolder = Path.Combine(tempDir, "ebooks");
        Directory.CreateDirectory(ebookFolder);
        var settings = new AlexandriaSettings { PublicUrl = "http://localhost", DataFolder = Path.Combine(tempDir, "data"), EbookFolder = ebookFolder, AudiobookFolder = Path.Combine(tempDir, "audiobooks") };
        converter = new FileCbrComicConverter(settings, new NullProgressService(), NullLogger<FileCbrComicConverter>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public async Task CbrConverter_Convert_RepacksEveryEntryAsCbzAndDeletesCbr()
    {
        var page1 = ComicFixtures.GrayPage(1);
        var page2 = ComicFixtures.GrayPage(2);
        var comicInfo = "<?xml version=\"1.0\"?><ComicInfo><Title>R</Title></ComicInfo>"u8.ToArray();
        var cbr = ComicFixtures.CreateStoredRar(ebookFolder, "Saga #1.cbr", [("Issue 1/002.jpg", page2), ("Issue 1/001.jpg", page1), ("ComicInfo.xml", comicInfo)]);

        await converter.Convert([], CancellationToken.None);

        Assert.IsFalse(File.Exists(cbr));
        var cbz = Path.Combine(ebookFolder, "Saga #1.cbz");
        using var zip = ZipFile.OpenRead(cbz);
        CollectionAssert.AreEquivalent(new[] { "Issue 1/002.jpg", "Issue 1/001.jpg", "ComicInfo.xml" }, zip.Entries.Select(e => e.FullName).ToArray());
        CollectionAssert.AreEqual(page1, ReadAll(zip.GetEntry("Issue 1/001.jpg")!));
        CollectionAssert.AreEqual(comicInfo, ReadAll(zip.GetEntry("ComicInfo.xml")!));
        Assert.AreEqual(zip.GetEntry("Issue 1/001.jpg")!.Length, zip.GetEntry("Issue 1/001.jpg")!.CompressedLength, "page images are stored, not recompressed");
    }

    [TestMethod]
    public async Task CbrConverter_Convert_ExistingCbzWithSameNameIsLeftAloneAndCbrKept()
    {
        var cbr = ComicFixtures.CreateStoredRar(ebookFolder, "Dup.cbr", [("1.jpg", ComicFixtures.GrayPage(1))]);
        var cbz = ComicFixtures.CreateSimpleCbz(ebookFolder, "Dup.cbz");
        var before = File.ReadAllBytes(cbz);

        await converter.Convert([], CancellationToken.None);

        Assert.IsTrue(File.Exists(cbr));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(cbz));
    }

    [TestMethod]
    public async Task CbrConverter_Convert_CorruptArchiveIsQuarantinedAndOthersStillConvert()
    {
        var corrupt = Path.Combine(ebookFolder, "Broken.cbr");
        await File.WriteAllBytesAsync(corrupt, "definitely not a rar archive"u8.ToArray());
        ComicFixtures.CreateStoredRar(ebookFolder, "Good.cbr", [("1.jpg", ComicFixtures.GrayPage(1))]);

        await converter.Convert([], CancellationToken.None);

        Assert.IsFalse(File.Exists(corrupt));
        Assert.IsTrue(File.Exists(Path.Combine(ebookFolder, FileCbrComicConverter.FailedFolderName, "Broken.cbr")));
        Assert.IsFalse(File.Exists(Path.Combine(ebookFolder, "Broken.cbz")));
        Assert.IsTrue(File.Exists(Path.Combine(ebookFolder, "Good.cbz")));
        Assert.IsEmpty(Directory.GetFiles(ebookFolder, "*.converting"));
    }

    [TestMethod]
    public async Task CbrConverter_Convert_MissingFolderIsANoOp()
    {
        Directory.Delete(ebookFolder);

        await converter.Convert([], CancellationToken.None);

        Assert.IsFalse(Directory.Exists(ebookFolder));
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal sealed class NullProgressService : IProgressService
    {
        public void Subscribe(IProgress<object> sink) { }
        public void Report(string title, int count, int total) { }
        public void ReportSubTask(string title, int count, int total) { }
        public void ReportDone<T>(T doneObject) { }
    }
}
