using System.Diagnostics;
using System.Text;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Externals.Models;

namespace Alexandria.DataAccess.Externals;

public class FfmpegWrapper : IFfmpegWrapper
{
    /// <summary>
    /// Streams an audiobook chapter to the output stream as MP3. Non-MP3 sources are transcoded
    /// via libmp3lame; MP3 sources are remuxed without re-encoding.
    /// The cancellation token kills the ffmpeg process if the client disconnects.
    /// </summary>
    // Kobo devices do not support AAC/ADTS streams — chapters must be served as MP3.
    // AAC remux (-c:a copy -f adts) was tried but playback fails on the device (verified 2026-04-16).
    public async Task StreamChapter(string inputPath, double startTime, double endTime, string? codec, int? bitrate, Stream outputStream, CancellationToken cancellationToken)
    {
        var quality = codec == "mp3" ? (int?)null : SelectLameQuality(bitrate);
        var codecArgs = quality is null ? "-c:a copy" : $"-acodec libmp3lame -q:a {quality}";
        var duration = endTime - startTime;
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v quiet -y -ss {startTime} -i \"{inputPath}\" -t {duration} -vn {codecArgs} -f mp3 pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken);
            await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg chapter extraction failed with exit code {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    /// <summary>
    /// Transcodes a single chapter to an MP3 file at <paramref name="outputPath"/>. Codec/quality
    /// selection matches <see cref="StreamChapter"/> so cached files are byte-identical to what
    /// on-demand streaming would produce for the same source.
    /// </summary>
    public async Task TranscodeChapterToFile(string inputPath, double startTime, double endTime, string? codec, int? bitrate, string outputPath, CancellationToken cancellationToken)
    {
        var quality = codec == "mp3" ? (int?)null : SelectLameQuality(bitrate);
        var codecArgs = quality is null ? "-c:a copy" : $"-acodec libmp3lame -q:a {quality}";
        var duration = endTime - startTime;
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v quiet -y -ss {startTime} -i \"{inputPath}\" -t {duration} -vn {codecArgs} -f mp3 \"{outputPath}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        try
        {
            await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg chapter transcode failed with exit code {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    public async Task<string> GetMetadataJson(string inputPath)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v quiet -print_format json -show_format -show_chapters -show_streams \"{inputPath}\"",
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

    /// <summary>
    /// Writes metadata and chapters to an M4B file using FFMETADATA1 format. The ";FFMETADATA1"
    /// header line is mandatory — without it, ffmpeg treats the file as INI and silently drops chapters.
    /// TIMEBASE=1/1000 sets chapter times to milliseconds (the default nanoseconds causes integer
    /// overflow on long audiobooks). -map 0:a preserves all audio streams; -map 0:v? preserves
    /// optional existing cover art; -c copy avoids re-encoding.
    /// </summary>
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

    /// <summary>
    /// Writes metadata with a new cover image. -disposition:v:0 attached_pic marks the video stream
    /// as album art instead of a video track — without this, media players show a video player UI.
    /// -map 2:v attaches the cover from the third input (pipe). -map 0:a preserves audio unchanged.
    /// </summary>
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

    /// <summary>
    /// Writes metadata while stripping the cover image. Omitting -map 0:v (no video stream mapping)
    /// effectively removes the embedded cover art.
    /// </summary>
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

    /// <summary>
    /// Escapes special characters for FFMETADATA1 format. The characters =, ;, # and \ have
    /// syntactic meaning in FFMETADATA — unescaped values cause metadata injection or parsing errors.
    /// </summary>
    private static string EscapeFfMetadataValue(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("=", "\\=")
            .Replace(";", "\\;")
            .Replace("#", "\\#")
            .Replace("\n", "\\\n");
    }

    /// <summary>
    /// Picks a libmp3lame -q:a level that roughly matches the source AAC bitrate.
    /// Lower values = higher quality/slower/larger. Spoken word is forgiving so we
    /// avoid over-encoding low-bitrate sources.
    /// </summary>
    private static int SelectLameQuality(int? bitrate) => bitrate switch
    {
        <= 48000 => 8,   // ~85 kbps — source is very low quality
        <= 80000 => 6,   // ~130 kbps — standard audiobook (64 kbps AAC)
        <= 130000 => 4,  // ~165 kbps — high quality (128 kbps AAC)
        _ => 2           // ~190 kbps — very high bitrate source
    };

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
