using System.Text.Json;
using System.Threading.Channels;
using Alexandria.Api.Models.Events;
using Alexandria.Business.Library.Models.Others;

namespace Alexandria.Api.Sync;

/// <summary>
/// Singleton broker that sits between the hosted indexing service (producer) and any number
/// of SSE subscribers (consumers). Combines three concerns the old controller-owned design
/// split across <c>LibrarySyncLock</c>, <c>ChannelProgress</c>, and <c>IIndexingProgressService</c>:
/// (1) at-most-one-in-flight gating, (2) fan-out broadcast to N subscribers, (3) snapshot cache
/// so late joiners catch up instantly.
/// </summary>
public sealed class IndexingBroker : IIndexingBroker
{
    private readonly Channel<byte> runRequests = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropWrite
    });

    private readonly Lock gate = new();
    private readonly List<ChannelWriter<string>> subscribers = [];
    private readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private bool isRunning;
    private SyncProgressEvent? lastSnapshot;

    public bool IsRunning
    {
        get
        {
            lock (gate)
                return isRunning;
        }
    }

    public SyncProgressEvent? LastSnapshot
    {
        get
        {
            lock (gate)
                return lastSnapshot;
        }
    }

    /// <summary>
    /// Sets <see cref="IsRunning"/> eagerly under the lock so clicks from other tabs see "running"
    /// immediately, closing the race where TryEnqueueRun would accept duplicates in the tiny window
    /// between dequeue and BeginRun.
    /// </summary>
    public bool TryEnqueueRun()
    {
        lock (gate)
        {
            if (isRunning)
                return false;
            isRunning = true;
        }
        runRequests.Writer.TryWrite(0);
        return true;
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
        lock (gate)
        {
            isRunning = false;
            lastSnapshot = null;
            toComplete = [.. subscribers];
            subscribers.Clear();
        }
        foreach (var writer in toComplete)
            writer.TryComplete();
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
        var evt = new SyncProgressEvent(
            report.Title,
            report.Subtitle,
            report.Progress,
            report.Total,
            report.SubTask?.Title,
            report.SubTask?.Progress,
            report.SubTask?.Total);

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
        BroadcastTerminal(FormatFrame(new SyncCompleteEvent(true, bookCount, lastIndexedTimestamp)));
    }

    public void EmitError(string message)
    {
        BroadcastTerminal(FormatFrame(new SyncErrorEvent(true, true, message)));
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
