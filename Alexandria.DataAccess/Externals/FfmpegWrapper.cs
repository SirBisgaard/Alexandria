using System.Diagnostics;
using System.Text;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Externals.Models;

namespace Alexandria.DataAccess.Externals;

public class FfmpegWrapper : IFfmpegWrapper
{
    /// <summary>
    /// Streams an audiobook chapter to the output stream as MP3. Caller picks the libmp3lame
    /// quality via <paramref name="qaLevel"/>: pass <c>null</c> to remux without re-encoding
    /// (only valid for MP3 sources), or an integer 0–9 to transcode at that <c>-q:a</c> level.
    /// The cancellation token kills the ffmpeg process if the client disconnects.
    /// </summary>
    // Kobo devices do not support AAC/ADTS streams — chapters must be served as MP3.
    // AAC remux (-c:a copy -f adts) was tried but playback fails on the device (verified 2026-04-16).
    public async Task StreamChapter(string inputPath, double startTime, double endTime, int? qaLevel, Stream outputStream, CancellationToken cancellationToken)
    {
        var codecArgs = qaLevel is null ? "-c:a copy" : $"-acodec libmp3lame -q:a {qaLevel}";
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
    /// Transcodes a single chapter to an MP3 file at <paramref name="outputPath"/>. Caller picks
    /// the libmp3lame quality via <paramref name="qaLevel"/>: <c>null</c> to remux (MP3 source
    /// only), or 0–9 to transcode at that <c>-q:a</c> level. When <paramref name="tags"/> is
    /// provided, the inherited source metadata is stripped (<c>-map_metadata -1</c>) and
    /// replaced via an FFMETADATA file — no follow-up tag-write pass is needed. When
    /// <paramref name="coverPath"/> is provided, it's attached as album art in the same ffmpeg
    /// invocation (ID3v2.3 for MP3 cover compatibility). Passing nulls for both produces a
    /// metadata-free cache chunk suitable for the M4B TranscodeAtIndex flow.
    /// </summary>
    public async Task TranscodeChapterToFile(string inputPath, double startTime, double endTime, int? qaLevel, string outputPath, IReadOnlyDictionary<string, string>? tags, string? coverPath, CancellationToken cancellationToken)
    {
        var codecArgs = qaLevel is null ? "-c:a copy" : $"-acodec libmp3lame -q:a {qaLevel}";
        var duration = endTime - startTime;

        var hasTags = tags is not null && tags.Count > 0;
        var hasCover = !string.IsNullOrEmpty(coverPath) && File.Exists(coverPath);
        var metadataPath = outputPath + ".ffmetadata";

        try
        {
            if (hasTags)
                await File.WriteAllTextAsync(metadataPath, GenerateFfMetadata(tags!, []), Encoding.UTF8, cancellationToken);

            var sb = new StringBuilder("-v quiet -y ");
            sb.Append($"-ss {startTime} -i \"{inputPath}\" ");
            if (hasTags)
                sb.Append($"-f ffmetadata -i \"{metadataPath}\" ");
            if (hasCover)
                sb.Append($"-i \"{coverPath}\" ");
            sb.Append($"-t {duration} ");

            if (hasCover)
            {
                var coverInputIndex = hasTags ? 2 : 1;
                sb.Append($"-map 0:a -map {coverInputIndex}:v -c:v copy -disposition:v:0 attached_pic -metadata:s:v title=Cover -metadata:s:v comment=Cover ");
            }
            else
            {
                sb.Append("-vn ");
            }

            sb.Append(hasTags ? "-map_metadata 1 " : "-map_metadata -1 ");
            sb.Append($"{codecArgs} ");
            if (hasCover)
                sb.Append("-id3v2_version 3 ");
            sb.Append($"-f mp3 \"{outputPath}\"");

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = sb.ToString(),
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
        finally
        {
            if (File.Exists(metadataPath))
                File.Delete(metadataPath);
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

    private static string GenerateFfMetadata(IReadOnlyDictionary<string, string> tags, IReadOnlyList<FfmpegChapter> chapters)
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
    /// Libmp3lame <c>-q:a</c> level for **on-device streaming / pre-transcoded cache** paths.
    /// Fine-grained mapping that hits every VBR level from 9 (lowest) up to 2 (~190 kbps MP3).
    /// Spoken word is forgiving and Kobo devices don't benefit from audiophile quality, so the
    /// mapping caps at <c>-q:a 2</c> regardless of how high the source bitrate goes. Null bitrate
    /// is treated as the highest tier under the cap.
    /// </summary>
    public static int SelectLameQualityForStreaming(int? bitrate) => bitrate switch
    {
        <= 32000 => 9,
        <= 48000 => 8,
        <= 64000 => 7,
        <= 80000 => 6,
        <= 96000 => 5,
        <= 128000 => 4,
        <= 160000 => 3,
        _ => 2           // cap at ~190 kbps regardless of source
    };

    /// <summary>
    /// Libmp3lame <c>-q:a</c> level for **permanent M4B→MP3 conversion** (ConvertAtIndex mode).
    /// Same per-bucket mapping as <see cref="SelectLameQualityForStreaming"/> for sources up to
    /// 160 kbps, then continues down through <c>-q:a 1</c> (~225 kbps) and <c>-q:a 0</c>
    /// (~245 kbps, libmp3lame's ceiling) for higher-bitrate inputs — the output replaces the
    /// original on disk, so we preserve as much quality as the encoder can emit. Null bitrate
    /// is treated as the highest tier.
    /// </summary>
    public static int SelectLameQualityForConversion(int? bitrate) => bitrate switch
    {
        <= 32000 => 9,
        <= 48000 => 8,
        <= 64000 => 7,
        <= 80000 => 6,
        <= 96000 => 5,
        <= 128000 => 4,
        <= 160000 => 3,
        <= 200000 => 2,
        <= 256000 => 1,
        _ => 0
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
