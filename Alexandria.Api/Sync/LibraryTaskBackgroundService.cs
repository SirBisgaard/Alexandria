using Alexandria.Business.Library.Interfaces;

namespace Alexandria.Api.Sync;

/// <summary>
/// Long-lived hosted service that runs every background library task (sync, file-management, …).
/// Reads task types from the broker, resolves each in a fresh DI scope (so <see cref="IProgressService"/>
/// retains its scoped contract), wires the scoped progress service's sink to the broker, and routes
/// terminal + error frames back through the broker's fan-out.
/// </summary>
public sealed class LibraryTaskBackgroundService(ILibraryTaskBroker broker, IServiceScopeFactory scopeFactory, ILogger<LibraryTaskBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Type taskType;
            try
            {
                taskType = await broker.WaitForTask(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunOnce(taskType, stoppingToken);
        }
    }

    private async Task RunOnce(Type taskType, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var task = (IBackgroundTask)scope.ServiceProvider.GetRequiredService(taskType);
        var progress = scope.ServiceProvider.GetRequiredService<IProgressService>();

        progress.Subscribe(broker);
        broker.BeginRun(task);

        try
        {
            await task.Execute(progress, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background task {Kind} failed", task.Kind);
            broker.EmitError(ex);
        }
        finally
        {
            broker.EndRun();
        }
    }
}
