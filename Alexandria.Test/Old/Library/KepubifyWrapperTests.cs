using Alexandria.DataAccess.Externals;

namespace Alexandria.Tests.Old.Library;

[TestClass]
[Ignore]
public sealed class KepubifyWrapperTests
{
    private static readonly string ContentBooksDir = Path.Combine(AppContext.BaseDirectory, "Old", "TestData", "Books");

    private string outputDir = null!;

    [TestInitialize]
    public void Setup()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);
    }

    [TestMethod]
    public async Task ConvertAsync_ValidEpub_CreatesKepubFile()
    {
        var converter = new KepubifyWrapper();
        var inputPath = Directory.GetFiles(ContentBooksDir, "*.epub").First();
        var outputPath = Path.Combine(outputDir, "output.kepub.epub");

        await converter.Convert(inputPath, outputPath);

        Assert.IsTrue(File.Exists(outputPath), "Expected kepub file was not created.");
        Assert.IsGreaterThan(0, new FileInfo(outputPath).Length, "Output file is empty.");
    }

    [TestMethod]
    public async Task ConvertAsync_OutputDiffersFromInput()
    {
        var converter = new KepubifyWrapper();
        var inputPath = Directory.GetFiles(ContentBooksDir, "*.epub").First();
        var outputPath = Path.Combine(outputDir, "output.kepub.epub");

        await converter.Convert(inputPath, outputPath);

        var inputBytes = await File.ReadAllBytesAsync(inputPath);
        var outputBytes = await File.ReadAllBytesAsync(outputPath);

        CollectionAssert.AreNotEqual(inputBytes, outputBytes, "Converted file should differ from the original EPUB.");
    }

    [TestMethod]
    public async Task ConvertAsync_NonExistentInput_ThrowsFileNotFoundException()
    {
        var converter = new KepubifyWrapper();
        var bogusPath = Path.Combine(outputDir, "does-not-exist.epub");
        var outputPath = Path.Combine(outputDir, "output.kepub.epub");

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => converter.Convert(bogusPath, outputPath));
    }

    [TestMethod]
    public async Task ConvertAsync_MultipleBooks_AllConvertSuccessfully()
    {
        var converter = new KepubifyWrapper();
        var epubs = Directory.GetFiles(ContentBooksDir, "*.epub").Take(3).ToArray();

        foreach (var epub in epubs)
        {
            var outputPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(epub) + ".kepub.epub");

            await converter.Convert(epub, outputPath);

            Assert.IsTrue(File.Exists(outputPath), $"Kepub not created for {Path.GetFileName(epub)}");
        }
    }
}
