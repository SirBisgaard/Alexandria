using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Business.Library.Services;

/// <summary>
/// Default <see cref="IProgressService"/> implementation. Caches the last outer <see cref="ProgressTask"/>
/// so <see cref="ReportSubTask"/> can augment it without the publisher re-supplying the outer counters.
/// Designed for scoped DI lifetime: the consumer that owns the operation subscribes once, then every
/// publisher resolved within the same scope publishes through the same instance.
/// </summary>
public sealed class ProgressService : IProgressService
{
    private IProgress<object>? sink;
    private ProgressTask? lastOuter;

    public void Subscribe(IProgress<object> progressSink)
    {
        sink = progressSink;
    }

    public void Report(string title, int count, int total)
    {
        lastOuter = new ProgressTask { Title = title, Count = count, Total = total };
        sink?.Report(new ProgressReport { Task = lastOuter });
    }

    public void ReportSubTask(string title, int count, int total)
    {
        if (lastOuter is null)
            return;
        sink?.Report(new ProgressReport
        {
            Task = lastOuter,
            SubTask = new ProgressTask { Title = title, Count = count, Total = total }
        });
    }

    public void ReportDone<T>(T doneObject)
    {
        if (doneObject is not null)
            sink?.Report(doneObject);
    }
}
