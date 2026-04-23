using System.Text.Json;
using Alexandria.Api.Models.Events;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Api.Sync;

/// <summary>
/// Library-sync background task: runs <see cref="IIndexerService.Index"/>, then reports a
/// <see cref="SyncCompletion"/> terminal payload via <see cref="IProgressService.ReportDone"/>.
/// </summary>
public sealed class SyncTask(IIndexerService indexer, ISettingsService settingsService) : IBackgroundTask
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Kind => "sync";

    public async Task Execute(IProgressService progress, CancellationToken cancellationToken)
    {
        var bookCount = await indexer.Index();
        var settings = await settingsService.Get();
        progress.ReportDone(new SyncCompletion
        {
            BookCount = bookCount,
            LastIndexedTimestamp = settings.LastIndexedTimestamp?.ToString("O")
        });
    }

    public string FormatTerminal(object doneObject) => doneObject switch
    {
        SyncCompletion c => JsonSerializer.Serialize(new SyncCompleteEvent
        {
            Done = true,
            BookCount = c.BookCount,
            LastIndexedTimestamp = c.LastIndexedTimestamp
        }, JsonOptions),
        _ => throw new InvalidOperationException($"Unexpected terminal payload from sync task: {doneObject.GetType().Name}")
    };

    public string FormatError(Exception exception)
    {
        var message = exception is InvalidOperationException ? exception.Message : "Sync failed unexpectedly";
        return JsonSerializer.Serialize(new SyncErrorEvent { Done = true, Failed = true, Error = message }, JsonOptions);
    }
}
