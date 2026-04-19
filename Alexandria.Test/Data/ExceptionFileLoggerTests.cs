using System.Text.RegularExpressions;
using Alexandria.Business.Logging;
using Microsoft.Extensions.Logging;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class ExceptionFileLoggerTests
{
    private string tempDir = null!;
    private string logPath = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        logPath = Path.Combine(tempDir, "exceptions.log");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public void Log_ErrorWithException_WritesTimestampMessageAndExceptionToString()
    {
        Exception ex;
        try
        {
            throw new InvalidOperationException("Boom");
        }
        catch (InvalidOperationException caught)
        {
            ex = caught;
        }

        using (var provider = new ExceptionFileLoggerProvider(logPath))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogError(ex, "Operation failed");
        }

        var content = File.ReadAllText(logPath);
        Assert.IsTrue(
            Regex.IsMatch(content, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2} => Operation failed\r?\n"),
            $"First line did not match expected timestamp format. Actual: {content}");
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("Boom", content);
    }

    [TestMethod]
    public void Log_ErrorWithoutException_WritesNothing()
    {
        using (var provider = new ExceptionFileLoggerProvider(logPath))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogError("Message with no exception");
        }

        Assert.AreEqual(0, new FileInfo(logPath).Length);
    }

    [TestMethod]
    public void Log_Warning_WritesNothing()
    {
        using (var provider = new ExceptionFileLoggerProvider(logPath))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogWarning(new InvalidOperationException("Boom"), "Warning message");
        }

        Assert.AreEqual(0, new FileInfo(logPath).Length);
    }

    [TestMethod]
    public void Log_Information_WritesNothing()
    {
        using (var provider = new ExceptionFileLoggerProvider(logPath))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogInformation(new InvalidOperationException("Boom"), "Info message");
        }

        Assert.AreEqual(0, new FileInfo(logPath).Length);
    }

    [TestMethod]
    public void Log_TwoEntries_SeparatedByBlankLine()
    {
        using (var provider = new ExceptionFileLoggerProvider(logPath))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogError(BuildThrown("First"), "First op");
            logger.LogError(BuildThrown("Second"), "Second op");
        }

        var content = File.ReadAllText(logPath);
        var entries = SplitEntries(content);
        Assert.HasCount(2, entries);
        Assert.Contains("First op", entries[0]);
        Assert.Contains("First", entries[0]);
        Assert.Contains("Second op", entries[1]);
        Assert.Contains("Second", entries[1]);
    }

    [TestMethod]
    public void Provider_Dispose_ReleasesFileHandle()
    {
        var provider = new ExceptionFileLoggerProvider(logPath);
        provider.CreateLogger("Test").LogError(BuildThrown("X"), "msg");
        provider.Dispose();

        using var reopen = File.Open(logPath, FileMode.Open, FileAccess.Write, FileShare.None);
        Assert.IsTrue(reopen.CanWrite);
    }

    [TestMethod]
    public void Provider_SecondInstance_AppendsNotOverwrites()
    {
        using (var first = new ExceptionFileLoggerProvider(logPath))
            first.CreateLogger("Test").LogError(BuildThrown("first-boom"), "first entry");

        using (var second = new ExceptionFileLoggerProvider(logPath))
            second.CreateLogger("Test").LogError(BuildThrown("second-boom"), "second entry");

        var content = File.ReadAllText(logPath);
        Assert.Contains("first entry", content);
        Assert.Contains("first-boom", content);
        Assert.Contains("second entry", content);
        Assert.Contains("second-boom", content);
    }

    [TestMethod]
    public void Log_AfterFileDeleted_RecreatesFileAndWritesEntry()
    {
        using var provider = new ExceptionFileLoggerProvider(logPath);
        var logger = provider.CreateLogger("Test");

        logger.LogError(BuildThrown("first"), "first entry");
        Assert.IsTrue(File.Exists(logPath));

        File.Delete(logPath);
        Assert.IsFalse(File.Exists(logPath));

        logger.LogError(BuildThrown("second"), "second entry");

        Assert.IsTrue(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        Assert.Contains("second entry", content);
        Assert.Contains("second", content);
        Assert.IsFalse(content.Contains("first entry"), "Recreated file must not contain entries that were in the deleted file.");
    }

    [TestMethod]
    public void Log_Concurrent_EntriesAreNotInterleaved()
    {
        const int iterations = 500;

        using (var provider = new ExceptionFileLoggerProvider(logPath))
        {
            var logger = provider.CreateLogger("Test");
            Parallel.For(0, iterations, i => logger.LogError(BuildThrown($"boom-{i}"), $"op-{i}"));
        }

        var content = File.ReadAllText(logPath);
        var entries = SplitEntries(content);
        Assert.HasCount(iterations, entries);

        foreach (var entry in entries)
        {
            var lines = entry.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.IsGreaterThanOrEqualTo(2, lines.Length, "Each entry must contain at least the timestamp/message line and the exception line.");
            Assert.IsTrue(Regex.IsMatch(lines[0], @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2} => op-\d+"), $"First line malformed: {lines[0]}");
        }
    }

    private static Exception BuildThrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException caught)
        {
            return caught;
        }
    }

    private static List<string> SplitEntries(string content)
    {
        var normalized = content.Replace("\r\n", "\n");
        return normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim('\n'))
            .Where(e => e.Length > 0)
            .ToList();
    }
}
