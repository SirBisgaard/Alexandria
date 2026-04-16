using Alexandria.DataAccess.Externals.Models;

namespace Alexandria.DataAccess.Externals.Interfaces;

public interface IFfmpegHandler
{
    Task StreamChapter(string inputPath, double startTime, double endTime, string? codec, Stream outputStream, CancellationToken cancellationToken);
    Task<string> GetMetadataJson(string inputPath);
    Task<byte[]?> ExtractCoverImage(string inputPath);
    Task WriteMetadata(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters);
    Task WriteMetadataWithCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters, byte[] coverData);
    Task WriteMetadataRemoveCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters);
}
