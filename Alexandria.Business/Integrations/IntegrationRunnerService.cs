using Alexandria.Business.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Alexandria.Business.Integrations;

public sealed class IntegrationRunnerService(IServiceScopeFactory scopeFactory, ILibraryOperationLock operationLock, ILogger<IntegrationRunnerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAudioBookShelfJob(stoppingToken);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunAudioBookShelfJob(stoppingToken);
        }
    }

    private async Task RunAudioBookShelfJob(CancellationToken stoppingToken)
    {
        if (operationLock.IsBusy)
        {
            logger.LogInformation("Skipping AudioBookShelf sync; library operation in flight");
            return;
        }

        try
        {
            logger.LogInformation("AudioBookShelf sync job tick");
            using var scope = scopeFactory.CreateScope();
            var audioBookShelf = scope.ServiceProvider.GetRequiredService<IAudioBookShelfSyncIntegration>();
            await audioBookShelf.Sync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AudioBookShelf sync job failed");
        }
    }
}
