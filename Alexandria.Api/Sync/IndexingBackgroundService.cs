using Alexandria.Business.Library.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Alexandria.Api.Sync;

/// <summary>
/// Long-lived hosted service that owns library-sync execution. Reads run requests from the
/// broker, resolves indexing services in a fresh DI scope per run (so <see cref="IIndexingProgressService"/>
/// retains its scoped contract), and routes progress + terminal events through the broker.
/// </summary>
public sealed class IndexingBackgroundService(IIndexingBroker broker, IServiceScopeFactory scopeFactory, ILogger<IndexingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await broker.WaitForRunRequest(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunOnce();
        }
    }

    private async Task RunOnce()
    {
        using var scope = scopeFactory.CreateScope();
        var indexer = scope.ServiceProvider.GetRequiredService<IIndexerService>();
        var progressService = scope.ServiceProvider.GetRequiredService<IIndexingProgressService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        progressService.Subscribe(broker);
        broker.BeginRun();

        try
        {
            var bookCount = await indexer.Index();
            var settings = await settingsService.Get();
            broker.EmitComplete(bookCount, settings.LastIndexedTimestamp?.ToString("O"));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Library sync aborted before completion");
            broker.EmitError(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Library sync failed unexpectedly");
            broker.EmitError("Sync failed unexpectedly");
        }
        finally
        {
            broker.EndRun();
        }
    }
}
