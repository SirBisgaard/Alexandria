using System.Text.Json;
using Alexandria.Api.Models.Events;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Others;
using Alexandria.Business.Library.Services;

namespace Alexandria.Api.Sync;

/// <summary>
/// File-management background task: delegates to <see cref="IFileManagementService.Run"/>, which
/// emits its own <see cref="FileManagementCompletion"/> via <see cref="IProgressService.ReportDone"/>.
/// </summary>
public sealed class FileManagementTask(IFileManagementService service) : IBackgroundTask
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Kind => "file-management";

    public Task Execute(IProgressService progress, CancellationToken cancellationToken) => service.Run(cancellationToken);

    public string FormatTerminal(object doneObject) => doneObject switch
    {
        FileManagementCompletion c => JsonSerializer.Serialize(new FileManagementCompleteEvent
        {
            Done = true,
            Renamed = c.Renamed,
            Skipped = c.Skipped,
            Failed = c.Failed.Select(f => new FileManagementFailureEvent { BookUuid = f.BookUuid, Error = f.Error }).ToList()
        }, JsonOptions),
        _ => throw new InvalidOperationException($"Unexpected terminal payload from file-management task: {doneObject.GetType().Name}")
    };

    public string FormatError(Exception exception) => exception switch
    {
        FileManagementConflictException ce => JsonSerializer.Serialize(new FileManagementErrorEvent
        {
            Done = true,
            Failed = true,
            Error = ce.Message,
            Conflicts = ce.Conflicts
        }, JsonOptions),
        TemplateException te => JsonSerializer.Serialize(new FileManagementErrorEvent { Done = true, Failed = true, Error = te.Message }, JsonOptions),
        InvalidOperationException ioe => JsonSerializer.Serialize(new FileManagementErrorEvent { Done = true, Failed = true, Error = ioe.Message }, JsonOptions),
        _ => JsonSerializer.Serialize(new FileManagementErrorEvent { Done = true, Failed = true, Error = "File management failed unexpectedly." }, JsonOptions)
    };
}
