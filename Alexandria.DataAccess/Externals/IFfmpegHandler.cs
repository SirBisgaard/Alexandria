namespace Alexandria.DataAccess.Externals;

public interface IFfmpegHandler
{
    Task ExtractChapterAsMp3(string inputPath, double startTime, double endTime, string outputPath);
    Task<byte[]> ExtractChapterAsMp3ToMemory(string inputPath, double startTime, double endTime);
    Task<string> GetMetadataJson(string inputPath);
    Task<byte[]?> ExtractCoverImage(string inputPath);
}
