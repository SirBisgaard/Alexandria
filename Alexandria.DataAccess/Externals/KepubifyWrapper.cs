using System.Diagnostics;
using Alexandria.DataAccess.Externals.Interfaces;

namespace Alexandria.DataAccess.Externals;

public class KepubifyWrapper : IKepubifyWrapper
{
    private readonly string kepubifyPath;

    public KepubifyWrapper()
    {
        kepubifyPath = Path.Combine(AppContext.BaseDirectory, "kepubify");

        if (!File.Exists(kepubifyPath))
            throw new FileNotFoundException("Kepubify binary not found.", kepubifyPath);
    }

    public async Task Convert(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("EPUB file not found.", inputPath);

        if (!File.Exists(kepubifyPath))
            throw new FileNotFoundException("Kepubify binary not found.", kepubifyPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = kepubifyPath,
            Arguments = $"-o \"{outputPath}\" \"{inputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Kepubify failed with exit code {process.ExitCode}: {error}");
    }
}