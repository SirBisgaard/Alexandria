using Alexandria.DataAccess.Externals.Models;

namespace Alexandria.DataAccess.Externals.Interfaces;

public interface IFfmpegWrapper
{
    Task StreamChapter(string inputPath, double startTime, double endTime, string? codec, int? bitrate, Stream outputStream, CancellationToken cancellationToken);
    Task TranscodeChapterToFile(string inputPath, double startTime, double endTime, string? codec, int? bitrate, string outputPath, IReadOnlyDictionary<string, string>? tags, string? coverPath, CancellationToken cancellationToken);
    Task<string> GetMetadataJson(string inputPath);
    Task<byte[]?> ExtractCoverImage(string inputPath);
    Task WriteMetadata(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters);
    Task WriteMetadataWithCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters, byte[] coverData);
    Task WriteMetadataRemoveCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters);
}
