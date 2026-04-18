using System.IO.Compression;
using Alexandria.DataAccess.Externals;
using Alexandria.Tests.TestData;
using Microsoft.Extensions.Logging;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class KepubifyWrapperTests
{
    private string tempDir = null!;
    private CapturingLogger<KepubifyWrapper> logger = null!;
    private KepubifyWrapper wrapper = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        logger = new CapturingLogger<KepubifyWrapper>();
        wrapper = new KepubifyWrapper(logger);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public async Task Kepubify_EpubInput_WritesValidKepubFileAtTargetPath()
    {
        var epubPath = Fixtures.CopyEpubTo(tempDir);
        var outputPath = Path.Combine(tempDir, "Peter Pan.kepub.epub");

        await wrapper.Convert(epubPath, outputPath);

        Assert.IsTrue(File.Exists(outputPath), "Output file should exist at the caller-specified path.");
        Assert.IsGreaterThan(0, new FileInfo(outputPath).Length);

        using var stream = File.OpenRead(outputPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.IsNotNull(zip.GetEntry("META-INF/container.xml"), "KEPUB output must be a valid EPUB archive.");
        Assert.IsTrue(
            zip.Entries.Any(e => e.FullName.Contains(".kobo/", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("cover.xhtml", StringComparison.OrdinalIgnoreCase)),
            "KEPUB-specific structure (.kobo directory or kepubify-inserted cover page) should be present.");
    }

    [TestMethod]
    public void Kepubify_BundledBinary_ExistsAndIsExecutable()
    {
        var binaryPath = Path.Combine(AppContext.BaseDirectory, "kepubify");

        Assert.IsTrue(File.Exists(binaryPath), "Kepubify binary must be copied to the test output directory.");

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var mode = File.GetUnixFileMode(binaryPath);
            Assert.IsTrue((mode & UnixFileMode.UserExecute) != 0, "Kepubify binary must have owner-execute permission on Unix.");
        }
    }

    [TestMethod]
    public async Task Kepubify_NonExistentInputFile_LogsErrorAndThrows()
    {
        var missingInput = Path.Combine(tempDir, "does-not-exist.epub");
        var outputPath = Path.Combine(tempDir, "out.kepub.epub");

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            async () => await wrapper.Convert(missingInput, outputPath));

        var errors = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.HasCount(1, errors);
        Assert.IsInstanceOfType<FileNotFoundException>(errors[0].Exception);
        Assert.Contains(missingInput, errors[0].Message);
    }
}
