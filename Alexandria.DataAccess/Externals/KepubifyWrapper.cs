using System.Diagnostics;
using Alexandria.DataAccess.Externals.Interfaces;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Externals;

public class KepubifyWrapper : IKepubifyWrapper
{
    private readonly ILogger<KepubifyWrapper> logger;
    private readonly string kepubifyPath;

    public KepubifyWrapper(ILogger<KepubifyWrapper> logger)
    {
        this.logger = logger;
        kepubifyPath = Path.Combine(AppContext.BaseDirectory, "kepubify");

        if (!File.Exists(kepubifyPath))
            throw new FileNotFoundException("Kepubify binary not found.", kepubifyPath);
    }

    public async Task Convert(string inputPath, string outputPath)
    {
        try
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Kepubify convert failed for {Input}", inputPath);
            throw;
        }
    }
}
