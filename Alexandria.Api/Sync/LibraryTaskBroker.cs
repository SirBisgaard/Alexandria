using System.Text.Json;
using System.Threading.Channels;
using Alexandria.Api.Models.Events;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Api.Sync;

/// <summary>
/// Default <see cref="ILibraryTaskBroker"/>. Owns four concerns: (1) the 1-deep dispatch queue
/// that triggers the hosted background service, (2) fan-out broadcast to N SSE subscribers,
/// (3) snapshot cache so late joiners catch up instantly, (4) mapping from the progress
/// service's reports to wire-level SSE frames via the current task's formatter. Exclusivity
/// is delegated to <see cref="ILibraryOperationLock"/> so tasks of any kind share one busy-state.
/// </summary>
public sealed class LibraryTaskBroker(ILibraryOperationLock libraryLock) : ILibraryTaskBroker
{
    private readonly Channel<Type> taskRequests = Channel.CreateBounded<Type>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    private readonly Lock gate = new();
    private readonly List<ChannelWriter<string>> subscribers = [];
    private readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private IDisposable? activeLease;
    private IBackgroundTask? currentTask;
    private ProgressEvent? lastSnapshot;

    public bool IsRunning => libraryLock.IsBusy;

    public string? RunningKind
    {
        get
        {
            lock (gate)
                return currentTask?.Kind;
        }
    }

    public ProgressEvent? LastSnapshot
    {
        get
        {
            lock (gate)
                return lastSnapshot;
        }
    }

    /// <summary>
    /// Reserves the shared library-operation lease eagerly so clicks from other tabs see "running"
    /// immediately, closing the race where a second enqueue would sneak in between dequeue and
    /// BeginRun. If the lease can't be acquired (another op in flight), returns false without
    /// writing to the channel.
    /// </summary>
    public bool TryEnqueue(Type taskType)
    {
        if (!typeof(IBackgroundTask).IsAssignableFrom(taskType))
            throw new ArgumentException($"{taskType} does not implement {nameof(IBackgroundTask)}.", nameof(taskType));

        if (!libraryLock.TryBeginExclusive(out var lease))
            return false;

        lock (gate)
            activeLease = lease;

        if (taskRequests.Writer.TryWrite(taskType))
            return true;

        lock (gate)
            activeLease = null;
        lease.Dispose();
        return false;
    }

    public async Task<Type> WaitForTask(CancellationToken cancellationToken)
    {
        return await taskRequests.Reader.ReadAsync(cancellationToken);
    }

    public void BeginRun(IBackgroundTask task)
    {
        lock (gate)
        {
            currentTask = task;
            lastSnapshot = null;
        }
    }

    public void EndRun()
    {
        List<ChannelWriter<string>> toComplete;
        IDisposable? leaseToRelease;
        lock (gate)
        {
            currentTask = null;
            lastSnapshot = null;
            leaseToRelease = activeLease;
            activeLease = null;
            toComplete = [.. subscribers];
            subscribers.Clear();
        }
        foreach (var writer in toComplete)
            writer.TryComplete();
        leaseToRelease?.Dispose();
    }

    public LibraryTaskSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        ProgressEvent? snapshot;
        lock (gate)
        {
            subscribers.Add(channel.Writer);
            snapshot = lastSnapshot;
        }

        if (snapshot is not null)
            channel.Writer.TryWrite(FormatFrame(snapshot));

        return new LibraryTaskSubscription(channel.Reader, () =>
        {
            lock (gate)
                subscribers.Remove(channel.Writer);
            channel.Writer.TryComplete();
        });
    }

    /// <summary>
    /// <see cref="IProgress{T}.Report"/> hook wired up through <c>IProgressService.Subscribe</c>.
    /// <see cref="ProgressReport"/> ticks are mapped to <see cref="ProgressEvent"/> and fan-out;
    /// any other object is treated as a terminal payload and routed through the current task's
    /// <see cref="IBackgroundTask.FormatTerminal"/>.
    /// </summary>
    public void Report(object value)
    {
        if (value is ProgressReport report)
        {
            var evt = new ProgressEvent
            {
                Title = report.Task.Title,
                Count = report.Task.Count,
                Total = report.Task.Total,
                SubTaskTitle = report.SubTask?.Title,
                SubCount = report.SubTask?.Count,
                SubTotal = report.SubTask?.Total
            };

            var frame = FormatFrame(evt);
            List<ChannelWriter<string>> snapshotSubs;
            lock (gate)
            {
                lastSnapshot = evt;
                snapshotSubs = [.. subscribers];
            }
            foreach (var writer in snapshotSubs)
                writer.TryWrite(frame);
            return;
        }

        IBackgroundTask? task;
        lock (gate)
            task = currentTask;
        if (task is null)
            return;
        BroadcastTerminal(task.FormatTerminal(value));
    }

    public void EmitError(Exception exception)
    {
        IBackgroundTask? task;
        lock (gate)
            task = currentTask;
        if (task is null)
            return;
        BroadcastTerminal(task.FormatError(exception));
    }

    /// <summary>
    /// Writes the terminal frame to all current subscribers without completing the writers —
    /// <see cref="EndRun"/> completes them afterwards so readers drain the terminal frame first.
    /// DropOldest channels guarantee TryWrite succeeds even under pressure.
    /// </summary>
    private void BroadcastTerminal(string frameBody)
    {
        var frame = $"data: {frameBody}\n\n";
        List<ChannelWriter<string>> snapshotSubs;
        lock (gate)
            snapshotSubs = [.. subscribers];
        foreach (var writer in snapshotSubs)
            writer.TryWrite(frame);
    }

    private string FormatFrame<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, jsonOptions);
        return $"data: {json}\n\n";
    }
}
