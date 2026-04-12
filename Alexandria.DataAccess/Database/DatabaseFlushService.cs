using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Database;

public class DatabaseFlushService(SqliteConnectionProvider provider, ILogger<DatabaseFlushService> logger) : IHostedService, IDisposable
{
    private Timer? timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        timer = new Timer(FlushCallback, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        timer?.Change(Timeout.Infinite, 0);
        await provider.Flush();
        logger.LogInformation("Database flushed to disk on shutdown.");
    }

    private async void FlushCallback(object? state)
    {
        try
        {
            await provider.Flush();
            logger.LogInformation("Periodic database flush completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Periodic database flush failed.");
        }
    }

    public void Dispose() => timer?.Dispose();
}
