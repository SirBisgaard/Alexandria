using Alexandria.Business.KoboStoreProxy.Extensions;
using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Tests.Business;

[TestClass]
public sealed class BookActivityEventExtensionsTests
{
    private const string Kobo = "kobo-device";
    private static readonly Guid BookUuid = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 5, 8, 15, 18, 45, TimeSpan.Zero);

    [TestMethod]
    public void ToActivityEntries_StartAndPauseHoursApart_CollapsesIntoOneSession()
    {
        var events = NewestFirst(
            Playback("start", Start, "Start", "0.0152631", listened: "0"),
            Player("open", Start, "OpenPlayerScreen"),
            Playback("pause", Start.AddSeconds(9093), "Pause", "0.0982713", listened: "9092"));

        var entry = Assert.ContainsSingle(events.ToActivityEntries());

        Assert.AreEqual("PlaybackSession", entry.EventType);
        Assert.AreEqual("start", entry.EventId);
        Assert.AreEqual(Start, entry.ReceivedAt);
        Assert.AreEqual(Start.AddSeconds(9093), entry.EventTimestamp);
        Assert.AreEqual("Listened 2h 31m · 1.5% → 9.8% · session 2h 31m", entry.Description);
    }

    [TestMethod]
    public void ToActivityEntries_ResumeWithinGapAfterPause_ExtendsSession()
    {
        var events = NewestFirst(
            Playback("start-1", Start, "Start", "0.10", listened: "0"),
            Playback("pause-1", Start.AddMinutes(10), "Pause", "0.11", listened: "600"),
            Playback("start-2", Start.AddMinutes(40), "Start", "0.11", listened: "0"),
            Playback("pause-2", Start.AddMinutes(50), "Pause", "0.12", listened: "600"));

        var entry = Assert.ContainsSingle(events.ToActivityEntries());

        Assert.AreEqual("Listened 20m · 10.0% → 12.0% · session 50m", entry.Description);
    }

    [TestMethod]
    public void ToActivityEntries_ResumeBeyondGapAfterPause_StartsNewSession()
    {
        var events = NewestFirst(
            Playback("start-1", Start, "Start", "0.10", listened: "0"),
            Playback("pause-1", Start.AddMinutes(10), "Pause", "0.11", listened: "600"),
            Playback("start-2", Start.AddMinutes(48), "Start", "0.11", listened: "0"),
            Playback("pause-2", Start.AddMinutes(58), "Pause", "0.12", listened: "600"));

        var entries = events.ToActivityEntries();

        Assert.HasCount(2, entries);
        Assert.AreEqual("start-2", entries[0].EventId);
        Assert.AreEqual("start-1", entries[1].EventId);
    }

    [TestMethod]
    public void ToActivityEntries_OtherEventInBetween_SplitsSessions()
    {
        var events = NewestFirst(
            Playback("start-1", Start, "Start", "0.10", listened: "0"),
            Playback("pause-1", Start.AddMinutes(10), "Pause", "0.11", listened: "600"),
            Player("finished", Start.AddMinutes(11), "MarkAsFinished"),
            Playback("start-2", Start.AddMinutes(12), "Start", "0.11", listened: "0"),
            Playback("pause-2", Start.AddMinutes(20), "Pause", "0.12", listened: "480"));

        var entries = events.ToActivityEntries();

        Assert.HasCount(3, entries);
        Assert.AreEqual("MarkAsFinished", entries[1].EventType);
    }

    [TestMethod]
    public void ToActivityEntries_OtherDevice_StartsNewSession()
    {
        var events = NewestFirst(
            Playback("start-1", Start, "Start", "0.10", listened: "0"),
            Playback("pause-1", Start.AddMinutes(10), "Pause", "0.11", listened: "600") with { DeviceUuid = "other-kobo" });

        Assert.HasCount(2, events.ToActivityEntries());
    }

    [TestMethod]
    public void ToActivityEntries_SinglePlayerEvent_IsNotWrapped()
    {
        var events = NewestFirst(Playback("pause", Start, "Pause", "0.0982713", listened: "9092"));

        var entry = Assert.ContainsSingle(events.ToActivityEntries());

        Assert.AreEqual("Playback", entry.EventType);
        Assert.AreEqual("Paused at 9.8% (? in) — listened 2h 31m 32s", entry.Description);
    }

    private static List<BookActivityEvent> NewestFirst(params BookActivityEvent[] chronological) => chronological.Reverse().ToList();

    private static BookActivityEvent Playback(string eventId, DateTimeOffset timestamp, string description, string progressPercent, string listened) =>
        Build(eventId, timestamp, "Playback", $$$"""{"Attributes":{"Description":"{{{description}}}","ProgressPercent":"{{{progressPercent}}}","SecondsListened":"{{{listened}}}"},"Metrics":{}}""");

    private static BookActivityEvent Player(string eventId, DateTimeOffset timestamp, string eventType) =>
        Build(eventId, timestamp, eventType, """{"Attributes":{},"Metrics":{}}""");

    private static BookActivityEvent Build(string eventId, DateTimeOffset timestamp, string eventType, string body) => new()
    {
        EventId = eventId,
        BookUuid = BookUuid,
        DeviceUuid = Kobo,
        EventType = eventType,
        EventTimestamp = timestamp,
        Body = body,
        ReceivedAt = timestamp
    };
}
