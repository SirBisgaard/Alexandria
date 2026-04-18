using System.Text.Json;
using Alexandria.DataAccess.Externals;
using Alexandria.Tests.TestData;
using Microsoft.Extensions.Logging;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class FfmpegWrapperTests
{
    private string tempDir = null!;
    private string m4bPath = null!;
    private CapturingLogger<FfmpegWrapper> logger = null!;
    private FfmpegWrapper ffmpeg = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "alexandria-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        m4bPath = Fixtures.CopyM4BTo(tempDir);
        logger = new CapturingLogger<FfmpegWrapper>();
        ffmpeg = new FfmpegWrapper(logger);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [TestMethod]
    public async Task GetMetadataJson_M4BFixture_ReturnsFormatStreamsAndChapters()
    {
        var json = await ffmpeg.GetMetadataJson(m4bPath);
        using var doc = JsonDocument.Parse(json);

        Assert.IsTrue(doc.RootElement.TryGetProperty("format", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("streams", out var streams));
        Assert.IsTrue(doc.RootElement.TryGetProperty("chapters", out _));
        Assert.IsNotEmpty(streams.EnumerateArray().ToList());
    }

    [TestMethod]
    public async Task ExtractCoverImage_M4BWithEmbeddedCover_ReturnsImageBytes()
    {
        var bytes = await ffmpeg.ExtractCoverImage(m4bPath);

        Assert.IsNotNull(bytes);
        Assert.IsGreaterThan(0, bytes.Length);
    }

    [TestMethod]
    public async Task ExtractCoverImage_AfterRemoveCover_ReturnsNull()
    {
        var tempPath = m4bPath + ".tmp";
        await ffmpeg.WriteMetadataRemoveCover(m4bPath, tempPath, [], []);
        File.Move(tempPath, m4bPath, overwrite: true);

        var bytes = await ffmpeg.ExtractCoverImage(m4bPath);

        Assert.IsNull(bytes);
    }

    [TestMethod]
    public async Task StreamChapter_M4BFixture_ProducesMp3Output()
    {
        using var output = new MemoryStream();

        await ffmpeg.StreamChapter(m4bPath, startTime: 0, endTime: 1, qaLevel: 4, output, CancellationToken.None);

        Assert.IsGreaterThan(0, output.Length);
        var bytes = output.ToArray();
        var hasId3 = bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33;
        var hasMpegFrame = bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0;
        Assert.IsTrue(hasId3 || hasMpegFrame, "Streamed output should start with an ID3 tag or MPEG frame sync.");
    }

    [TestMethod]
    public async Task StreamChapter_AlreadyCanceledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var output = new MemoryStream();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await ffmpeg.StreamChapter(m4bPath, 0, 1, 4, output, cts.Token));
    }

    [TestMethod]
    public async Task WriteMetadata_TagsAndChapters_RoundTripThroughFfmetadata()
    {
        var tags = new Dictionary<string, string>
        {
            ["title"] = "Round-trip Title",
            ["artist"] = "Round-trip Artist"
        };
        var chapters = new List<Alexandria.DataAccess.Externals.Models.FfmpegChapter>
        {
            new(0, 4000, "Chapter One"),
            new(4000, 8000, "Chapter Two")
        };

        var tempPath = m4bPath + ".tmp";
        await ffmpeg.WriteMetadata(m4bPath, tempPath, tags, chapters);
        File.Move(tempPath, m4bPath, overwrite: true);

        var json = await ffmpeg.GetMetadataJson(m4bPath);
        using var doc = JsonDocument.Parse(json);
        var formatTags = doc.RootElement.GetProperty("format").GetProperty("tags");
        Assert.AreEqual("Round-trip Title", formatTags.GetProperty("title").GetString());
        Assert.AreEqual("Round-trip Artist", formatTags.GetProperty("artist").GetString());

        var parsedChapters = doc.RootElement.GetProperty("chapters").EnumerateArray().ToList();
        Assert.HasCount(2, parsedChapters);
        Assert.AreEqual("Chapter One", parsedChapters[0].GetProperty("tags").GetProperty("title").GetString());
        Assert.AreEqual("Chapter Two", parsedChapters[1].GetProperty("tags").GetProperty("title").GetString());
    }

    [TestMethod]
    public async Task WriteMetadata_ValueContainingFfMetadataSpecials_Escapes()
    {
        var specialValue = "has = and ; and # and \\ backslash";
        var tags = new Dictionary<string, string> { ["comment"] = specialValue };

        var tempPath = m4bPath + ".tmp";
        await ffmpeg.WriteMetadata(m4bPath, tempPath, tags, []);
        File.Move(tempPath, m4bPath, overwrite: true);

        var json = await ffmpeg.GetMetadataJson(m4bPath);
        using var doc = JsonDocument.Parse(json);
        var comment = doc.RootElement.GetProperty("format").GetProperty("tags").GetProperty("comment").GetString();
        Assert.AreEqual(specialValue, comment);
    }

    [TestMethod]
    public async Task WriteMetadataWithCover_SetsAttachedPicDispositionOnVideoStream()
    {
        var coverPath = Fixtures.CopyCoverTo(tempDir);
        var coverBytes = await File.ReadAllBytesAsync(coverPath);

        var tempPath = m4bPath + ".tmp";
        await ffmpeg.WriteMetadataWithCover(m4bPath, tempPath, [], [], coverBytes);
        File.Move(tempPath, m4bPath, overwrite: true);

        var json = await ffmpeg.GetMetadataJson(m4bPath);
        using var doc = JsonDocument.Parse(json);
        var videoStream = doc.RootElement.GetProperty("streams").EnumerateArray()
            .FirstOrDefault(s => s.TryGetProperty("codec_type", out var ct) && ct.GetString() == "video");
        Assert.AreNotEqual(JsonValueKind.Undefined, videoStream.ValueKind, "Expected a video stream carrying the attached cover.");
        Assert.AreEqual(1, videoStream.GetProperty("disposition").GetProperty("attached_pic").GetInt32());
    }

    [DataTestMethod]
    [DataRow(null, 2)]
    [DataRow(320000, 2)]
    [DataRow(200000, 2)]
    [DataRow(160001, 2)]
    [DataRow(160000, 3)]
    [DataRow(128001, 3)]
    [DataRow(128000, 4)]
    [DataRow(96001, 4)]
    [DataRow(96000, 5)]
    [DataRow(80001, 5)]
    [DataRow(80000, 6)]
    [DataRow(64001, 6)]
    [DataRow(64000, 7)]
    [DataRow(48001, 7)]
    [DataRow(48000, 8)]
    [DataRow(32001, 8)]
    [DataRow(32000, 9)]
    [DataRow(0, 9)]
    public void SelectLameQualityForStreaming_Bitrate_MapsToCapAt2(int? bitrate, int expectedQuality)
    {
        Assert.AreEqual(expectedQuality, FfmpegWrapper.SelectLameQualityForStreaming(bitrate));
    }

    [DataTestMethod]
    [DataRow(null, 0)]
    [DataRow(320000, 0)]
    [DataRow(256001, 0)]
    [DataRow(256000, 1)]
    [DataRow(200001, 1)]
    [DataRow(200000, 2)]
    [DataRow(160001, 2)]
    [DataRow(160000, 3)]
    [DataRow(128001, 3)]
    [DataRow(128000, 4)]
    [DataRow(96001, 4)]
    [DataRow(96000, 5)]
    [DataRow(80001, 5)]
    [DataRow(80000, 6)]
    [DataRow(64001, 6)]
    [DataRow(64000, 7)]
    [DataRow(48001, 7)]
    [DataRow(48000, 8)]
    [DataRow(32001, 8)]
    [DataRow(32000, 9)]
    [DataRow(0, 9)]
    public void SelectLameQualityForConversion_Bitrate_MapsToFullRange(int? bitrate, int expectedQuality)
    {
        Assert.AreEqual(expectedQuality, FfmpegWrapper.SelectLameQualityForConversion(bitrate));
    }

    [TestMethod]
    public async Task StreamChapter_NullQaLevel_ProducesMp3OutputViaRemux()
    {
        var mp3Path = Fixtures.CopyMp3To(tempDir);
        using var output = new MemoryStream();

        await ffmpeg.StreamChapter(mp3Path, startTime: 0, endTime: 2, qaLevel: null, output, CancellationToken.None);

        var bytes = output.ToArray();
        Assert.IsGreaterThan(0, bytes.Length);
        var hasId3 = bytes.Length >= 3 && bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33;
        var hasMpegFrame = bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0;
        Assert.IsTrue(hasId3 || hasMpegFrame, "Remuxed output must still be a valid MP3 stream.");
    }

    [TestMethod]
    public async Task TranscodeChapterToFile_NullQaLevel_ProducesMp3FileViaRemux()
    {
        var mp3Path = Fixtures.CopyMp3To(tempDir);
        var outputPath = Path.Combine(tempDir, "chapter0.mp3");

        await ffmpeg.TranscodeChapterToFile(mp3Path, startTime: 0, endTime: 2, qaLevel: null, outputPath, tags: null, coverPath: null, CancellationToken.None);

        Assert.IsTrue(File.Exists(outputPath));
        var bytes = await File.ReadAllBytesAsync(outputPath);
        Assert.IsGreaterThan(0, bytes.Length);

        var json = await ffmpeg.GetMetadataJson(outputPath);
        using var doc = JsonDocument.Parse(json);
        var audioStream = doc.RootElement.GetProperty("streams").EnumerateArray()
            .FirstOrDefault(s => s.TryGetProperty("codec_type", out var ct) && ct.GetString() == "audio");
        Assert.AreNotEqual(JsonValueKind.Undefined, audioStream.ValueKind);
        Assert.AreEqual("mp3", audioStream.GetProperty("codec_name").GetString());
    }

    [TestMethod]
    public async Task GetMetadataJson_MissingFile_LogsErrorAndThrowsWithStderrInMessage()
    {
        var missingPath = Path.Combine(tempDir, "does-not-exist.m4b");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await ffmpeg.GetMetadataJson(missingPath));

        Assert.Contains("exit code", ex.Message);

        var errors = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.HasCount(1, errors);
        Assert.AreSame(ex, errors[0].Exception);
        Assert.Contains(missingPath, errors[0].Message);
    }

    [TestMethod]
    public async Task StreamChapter_NonZeroExit_LogsErrorAndThrowsWithStderrInMessage()
    {
        var garbagePath = Path.Combine(tempDir, "garbage.m4b");
        await File.WriteAllBytesAsync(garbagePath, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);
        using var output = new MemoryStream();

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await ffmpeg.StreamChapter(garbagePath, 0, 1, qaLevel: 4, output, CancellationToken.None));

        Assert.Contains("exit code", ex.Message);
        Assert.IsGreaterThan("ffmpeg chapter extraction failed with exit code 0: ".Length, ex.Message.Length);

        var errors = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.HasCount(1, errors);
        Assert.AreSame(ex, errors[0].Exception);
    }

    [TestMethod]
    public async Task StreamChapter_Canceled_LogsErrorAndRethrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var output = new MemoryStream();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await ffmpeg.StreamChapter(m4bPath, 0, 1, 4, output, cts.Token));

        var errors = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.HasCount(1, errors);
        Assert.IsInstanceOfType<OperationCanceledException>(errors[0].Exception);
    }

    [TestMethod]
    public async Task TranscodeChapterToFile_Canceled_LogsErrorAndRethrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var outputPath = Path.Combine(tempDir, "chapter0.mp3");

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await ffmpeg.TranscodeChapterToFile(m4bPath, 0, 1, qaLevel: 4, outputPath, tags: null, coverPath: null, cts.Token));

        var errors = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.HasCount(1, errors);
        Assert.IsInstanceOfType<OperationCanceledException>(errors[0].Exception);
    }

    [TestMethod]
    public async Task TranscodeChapterToFile_WithTagsAndCover_ProducesMp3WithMetadata()
    {
        var coverPath = Fixtures.CopyCoverTo(tempDir);
        var outputPath = Path.Combine(tempDir, "chapter0.mp3");
        var tags = new Dictionary<string, string>
        {
            ["title"] = "Transcoded Chapter",
            ["artist"] = "Transcoded Artist"
        };

        await ffmpeg.TranscodeChapterToFile(m4bPath, 0, 1, qaLevel: 4, outputPath, tags, coverPath, CancellationToken.None);

        Assert.IsTrue(File.Exists(outputPath));
        var bytes = await File.ReadAllBytesAsync(outputPath);
        Assert.IsGreaterThan(0, bytes.Length);

        var json = await ffmpeg.GetMetadataJson(outputPath);
        using var doc = JsonDocument.Parse(json);
        var outTags = doc.RootElement.GetProperty("format").GetProperty("tags");
        Assert.AreEqual("Transcoded Chapter", outTags.GetProperty("title").GetString());
        Assert.AreEqual("Transcoded Artist", outTags.GetProperty("artist").GetString());

        var videoStream = doc.RootElement.GetProperty("streams").EnumerateArray()
            .FirstOrDefault(s => s.TryGetProperty("codec_type", out var ct) && ct.GetString() == "video");
        Assert.AreNotEqual(JsonValueKind.Undefined, videoStream.ValueKind, "Cover should be attached as a video stream.");
        Assert.AreEqual(1, videoStream.GetProperty("disposition").GetProperty("attached_pic").GetInt32());
    }
}
