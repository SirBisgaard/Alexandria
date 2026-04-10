using System.Diagnostics;
using System.Text;

namespace Alexandria.DataAccess.Externals;

public class FfmpegHandler : IFfmpegHandler
{
    public async Task ExtractChapterAsMp3(string inputPath, double startTime, double endTime, string outputPath)
    {
        var duration = endTime - startTime;
        await RunFfmpeg($"-v quiet -y -ss {startTime} -i \"{inputPath}\" -t {duration} -vn -acodec libmp3lame -q:a 2 \"{outputPath}\"");
    }

    public async Task<byte[]> ExtractChapterAsMp3ToMemory(string inputPath, double startTime, double endTime)
    {
        var duration = endTime - startTime;
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v quiet -y -ss {startTime} -i \"{inputPath}\" -t {duration} -vn -acodec libmp3lame -q:a 2 -f mp3 pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        using var memoryStream = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(memoryStream);
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}: {error}");

        return memoryStream.ToArray();
    }

    public async Task<string> GetMetadataJson(string inputPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v quiet -print_format json -show_format -show_chapters \"{inputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe failed with exit code {process.ExitCode}: {error}");

        return output;
    }

    public async Task<byte[]?> ExtractCoverImage(string inputPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v quiet -i \"{inputPath}\" -an -vcodec copy -f image2pipe pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        using var memoryStream = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(memoryStream);
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var data = memoryStream.ToArray();
        return data.Length > 0 ? data : null;
    }

    public async Task WriteMetadata(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters)
    {
        var metadataPath = outputPath + ".ffmetadata";
        try
        {
            await File.WriteAllTextAsync(metadataPath, GenerateFfMetadata(tags, chapters), Encoding.UTF8);
            await RunFfmpeg($"-i \"{inputPath}\" -f ffmetadata -i \"{metadataPath}\" -map 0:a -map 0:v? -map_metadata 1 -map_metadata 0 -map_chapters 1 -c copy -f mp4 -y \"{outputPath}\"");
        }
        finally
        {
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
        }
    }

    public async Task WriteMetadataWithCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters, byte[] coverData)
    {
        var metadataPath = outputPath + ".ffmetadata";
        var coverPath = outputPath + ".cover.jpg";
        try
        {
            await File.WriteAllTextAsync(metadataPath, GenerateFfMetadata(tags, chapters), Encoding.UTF8);
            await File.WriteAllBytesAsync(coverPath, coverData);
            await RunFfmpeg($"-i \"{inputPath}\" -f ffmetadata -i \"{metadataPath}\" -i \"{coverPath}\" -map 0:a -map_metadata 1 -map_metadata 0 -map_chapters 1 -map 2:v -c copy -disposition:v:0 attached_pic -metadata:s:v title=Cover -metadata:s:v comment=Cover -f mp4 -y \"{outputPath}\"");
        }
        finally
        {
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
            if (File.Exists(coverPath))
                File.Delete(coverPath);
        }
    }

    public async Task WriteMetadataRemoveCover(string inputPath, string outputPath, Dictionary<string, string> tags, List<FfmpegChapter> chapters)
    {
        var metadataPath = outputPath + ".ffmetadata";
        try
        {
            await File.WriteAllTextAsync(metadataPath, GenerateFfMetadata(tags, chapters), Encoding.UTF8);
            await RunFfmpeg($"-i \"{inputPath}\" -f ffmetadata -i \"{metadataPath}\" -map 0:a -map_metadata 1 -map_metadata 0 -map_chapters 1 -c copy -f mp4 -y \"{outputPath}\"");
        }
        finally
        {
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
        }
    }

    private static string GenerateFfMetadata(Dictionary<string, string> tags, List<FfmpegChapter> chapters)
    {
        var sb = new StringBuilder();
        sb.AppendLine(";FFMETADATA1");

        foreach (var (key, value) in tags)
            sb.AppendLine($"{key}={EscapeFfMetadataValue(value)}");

        foreach (var chapter in chapters)
        {
            sb.AppendLine();
            sb.AppendLine("[CHAPTER]");
            sb.AppendLine("TIMEBASE=1/1000");
            sb.AppendLine($"START={chapter.StartMs}");
            sb.AppendLine($"END={chapter.EndMs}");
            sb.AppendLine($"title={EscapeFfMetadataValue(chapter.Title)}");
        }

        return sb.ToString();
    }

    private static string EscapeFfMetadataValue(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("=", "\\=")
            .Replace(";", "\\;")
            .Replace("#", "\\#")
            .Replace("\n", "\\\n");
    }

    private static async Task RunFfmpeg(string arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}: {error}");
    }
}
