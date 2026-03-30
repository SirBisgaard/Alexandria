using System.Diagnostics;

namespace Alexandria.DataAccess;

public class KepubifyConverter
{
    private readonly string kepubifyPath;

    public KepubifyConverter()
    {
        kepubifyPath = Path.Combine(AppContext.BaseDirectory, "kepubify");

        if (!File.Exists(kepubifyPath))
            throw new FileNotFoundException("Kepubify binary not found.", kepubifyPath);
    }

    public async Task ConvertAsync(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("EPUB file not found.", inputPath);

        if (!File.Exists(kepubifyPath))
            throw new FileNotFoundException("Kepubify binary not found.", kepubifyPath);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = kepubifyPath,
                Arguments = $"-o \"{outputPath}\" \"{inputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Kepubify failed with exit code {process.ExitCode}: {error}");
    }
}