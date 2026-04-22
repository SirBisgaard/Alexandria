using System.Text.Json;
using System.Threading.Channels;
using Alexandria.Api.Models.Events;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Api.Sync;

/// <summary>
/// Singleton broker between the hosted indexing service (producer) and any number of SSE
/// subscribers (consumers). Owns three concerns: (1) the 1-deep dispatch queue that triggers
/// the hosted service, (2) fan-out broadcast to N subscribers, (3) snapshot cache so late
/// joiners catch up instantly. Exclusivity is delegated to <see cref="ILibraryOperationLock"/>
/// so indexing and inline operations (e.g. file management) share a single busy-state.
/// </summary>
public sealed class IndexingBroker(ILibraryOperationLock libraryLock) : IIndexingBroker
{
    private readonly Channel<byte> runRequests = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    private readonly Lock gate = new();
    private readonly List<ChannelWriter<string>> subscribers = [];
    private readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private IDisposable? activeLease;
    private SyncProgressEvent? lastSnapshot;

    public bool IsRunning => libraryLock.IsBusy;

    public SyncProgressEvent? LastSnapshot
    {
        get
        {
            lock (gate)
                return lastSnapshot;
        }
    }

    /// <summary>
    /// Reserves the shared library-operation lease eagerly so clicks from other tabs see "running"
    /// immediately, closing the race where TryEnqueueRun would accept duplicates in the tiny window
    /// between dequeue and BeginRun. If the lease can't be acquired (another op in flight), returns
    /// false without writing to the channel.
    /// </summary>
    public bool TryEnqueueRun()
    {
        if (!libraryLock.TryBeginExclusive(out var lease))
            return false;

        lock (gate)
            activeLease = lease;

        if (runRequests.Writer.TryWrite(0))
            return true;

        // Channel was already full (another run dequeue in flight). Release the lease so the
        // already-pending run keeps exclusivity; we don't want two leases outstanding.
        lock (gate)
            activeLease = null;
        lease.Dispose();
        return false;
    }

    public async Task WaitForRunRequest(CancellationToken cancellationToken)
    {
        await runRequests.Reader.ReadAsync(cancellationToken);
    }

    public void BeginRun()
    {
        lock (gate)
            lastSnapshot = null;
    }

    public void EndRun()
    {
        List<ChannelWriter<string>> toComplete;
        IDisposable? leaseToRelease;
        lock (gate)
        {
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

    public IndexingSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        SyncProgressEvent? snapshot;
        lock (gate)
        {
            subscribers.Add(channel.Writer);
            snapshot = lastSnapshot;
        }

        if (snapshot is not null)
            channel.Writer.TryWrite(FormatFrame(snapshot));

        return new IndexingSubscription(channel.Reader, () =>
        {
            lock (gate)
                subscribers.Remove(channel.Writer);
            channel.Writer.TryComplete();
        });
    }

    public void Report(IndexingReport report)
    {
        var evt = new SyncProgressEvent
        {
            Title = report.Title,
            Subtitle = report.Subtitle,
            Progress = report.Progress,
            Total = report.Total,
            SubTaskTitle = report.SubTask?.Title,
            SubProgress = report.SubTask?.Progress,
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
    }

    public void EmitComplete(int bookCount, string? lastIndexedTimestamp)
    {
        BroadcastTerminal(FormatFrame(new SyncCompleteEvent
        {
            Done = true,
            BookCount = bookCount,
            LastIndexedTimestamp = lastIndexedTimestamp
        }));
    }

    public void EmitError(string message)
    {
        BroadcastTerminal(FormatFrame(new SyncErrorEvent { Done = true, Failed = true, Error = message }));
    }

    /// <summary>
    /// Writes the terminal frame to all current subscribers without completing the writers —
    /// <see cref="EndRun"/> completes them afterwards so readers drain the terminal frame first.
    /// DropOldest channels guarantee TryWrite succeeds even under pressure.
    /// </summary>
    private void BroadcastTerminal(string frame)
    {
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
