using System.Diagnostics;

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
