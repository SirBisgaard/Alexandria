using System.Globalization;
using System.Text.Json;
using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Extensions;

public static class BookActivityEventExtensions
{
    private const string PlaybackSessionEventType = "PlaybackSession";

    private static readonly TimeSpan PlaybackSessionGap = TimeSpan.FromMinutes(37);

    private static readonly HashSet<string> PlayerEventTypes = ["Playback", "OpenPlayerScreen", "TableOfContents", "PlaybackSpeedChange"];

    extension(IReadOnlyList<BookActivityEvent> events)
    {
        /// <summary>
        /// Turns a book's newest-first activity events into display entries, collapsing each Kobo
        /// audiobook listening session into a single entry. A session is a run of player events from
        /// the same device. The device reports nothing while audio is playing, so between a playback
        /// Start and its Pause there is no gap limit; after a Pause the next player event must follow
        /// within <see cref="PlaybackSessionGap"/>. Any other event ends the session. Listened time is
        /// the sum of the Pause events' SecondsListened; session time is wall-clock from first to
        /// last event, pauses included.
        /// </summary>
        public List<BookActivityEntry> ToActivityEntries()
        {
            var sessions = new List<List<BookActivityEvent>>();
            List<BookActivityEvent>? current = null;
            var playing = false;

            for (var i = events.Count - 1; i >= 0; i--)
            {
                var activityEvent = events[i];
                if (!PlayerEventTypes.Contains(activityEvent.EventType))
                {
                    sessions.Add([activityEvent]);
                    current = null;
                    continue;
                }

                var playback = ReadPlayback(activityEvent);
                if (current is not null && current[0].DeviceUuid == activityEvent.DeviceUuid && (playing || activityEvent.EventTimestamp - current[^1].EventTimestamp <= PlaybackSessionGap))
                {
                    current.Add(activityEvent);
                }
                else
                {
                    current = [activityEvent];
                    sessions.Add(current);
                    playing = false;
                }

                if (playback.Description == "Start")
                    playing = true;
                else if (playback.Description == "Pause")
                    playing = false;
            }

            sessions.Reverse();
            return sessions.Select(session => session.Count == 1 ? ToEntry(session[0]) : ToSessionEntry(session)).ToList();
        }
    }

    extension(BookActivityEvent activityEvent)
    {
        public string Describe()
        {
            // ABS sync writes plain-text log lines as Body — no JSON to parse, just render verbatim.
            if (activityEvent.EventType is "AbsProgressUpdated" or "AbsProgressCleared")
                return activityEvent.Body;

            JsonElement attrs;
            JsonElement metrics;
            try
            {
                using var doc = JsonDocument.Parse(activityEvent.Body);
                var root = doc.RootElement;
                attrs = root.TryGetProperty("Attributes", out var a) ? a.Clone() : default;
                metrics = root.TryGetProperty("Metrics", out var m) ? m.Clone() : default;
            }
            catch (JsonException)
            {
                return "";
            }

            return activityEvent.EventType switch
            {
                "Playback" => DescribePlayback(attrs, metrics),
                "OpenPlayerScreen" => "Opened player",
                "TableOfContents" => "Opened chapter list (" + (ReadString(attrs, "IsPlaying") == "true" ? "playing" : "paused") + ")",
                "ItemDetail" => "Viewed details from " + (ReadString(attrs, "Origin") ?? "?"),
                "ItemDetailsTabClicked" => $"Tapped \"{ReadString(attrs, "TabName") ?? "?"}\" tab in {ReadString(attrs, "Origin") ?? "?"}",
                "PlaybackSpeedChange" => $"Speed {ReadString(attrs, "OriginalSpeed") ?? "?"}× → {ReadString(attrs, "ResultSpeed") ?? "?"}×",
                "ShowRelatedPreviewBanner" => ReadLong(metrics, "index") is long idx ? $"Shown related-books banner #{idx}" : "Shown related-books banner",
                "StartReadingBook" => "Started this book for the first time",
                "OpenContent" => DescribeOpenContent(attrs),
                "LeaveContent" => DescribeLeaveContent(attrs, metrics),
                "MarkAsFinished" => "Marked as finished",
                "MarkAsUnread" => "Marked as unread",
                "BookProgress" => $"Reading progress: {ReadString(attrs, "progress") ?? "?"}%",
                _ => ""
            };
        }
    }

    private static BookActivityEntry ToEntry(BookActivityEvent activityEvent) => new()
    {
        EventId = activityEvent.EventId,
        EventType = activityEvent.EventType,
        EventTimestamp = activityEvent.EventTimestamp,
        ReceivedAt = activityEvent.ReceivedAt,
        Description = activityEvent.Describe()
    };

    private static BookActivityEntry ToSessionEntry(List<BookActivityEvent> session)
    {
        var playbacks = session.Select(ReadPlayback).ToList();
        var listened = playbacks.Where(p => p.Description == "Pause").Sum(p => p.SecondsListened ?? 0);
        var startPercent = playbacks.Select(p => p.ProgressPercent).FirstOrDefault(p => p is not null);
        var endPercent = playbacks.Select(p => p.ProgressPercent).LastOrDefault(p => p is not null);
        var start = session[0].EventTimestamp;
        var end = session[^1].EventTimestamp;

        return new BookActivityEntry
        {
            EventId = session[0].EventId,
            EventType = PlaybackSessionEventType,
            EventTimestamp = end,
            ReceivedAt = start,
            Description = $"Listened {FormatLength(listened)} · {FormatPercent(startPercent)} → {FormatPercent(endPercent)} · session {FormatLength((long)(end - start).TotalSeconds)}"
        };
    }

    private static PlaybackInfo ReadPlayback(BookActivityEvent activityEvent)
    {
        try
        {
            using var doc = JsonDocument.Parse(activityEvent.Body);
            var attrs = doc.RootElement.TryGetProperty("Attributes", out var a) ? a : default;
            return new PlaybackInfo
            {
                Description = ReadString(attrs, "Description"),
                ProgressPercent = ReadString(attrs, "ProgressPercent"),
                SecondsListened = long.TryParse(ReadString(attrs, "SecondsListened"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : null
            };
        }
        catch (JsonException)
        {
            return new PlaybackInfo();
        }
    }

    private static string FormatLength(long seconds)
    {
        if (seconds < 60)
            return $"{seconds}s";
        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }

    private static string DescribePlayback(JsonElement attrs, JsonElement metrics)
    {
        var description = ReadString(attrs, "Description");
        var percent = FormatPercent(ReadString(attrs, "ProgressPercent"));
        var elapsed = FormatElapsed(ReadLong(metrics, "ProgressTime"));
        if (description == "Start")
            return $"Started at {percent} ({elapsed} in)";
        if (description == "Pause")
        {
            var listened = long.TryParse(ReadString(attrs, "SecondsListened"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : (long?)null;
            var suffix = listened is not null ? $" — listened {FormatElapsed(listened)}" : "";
            return $"Paused at {percent} ({elapsed} in){suffix}";
        }
        return $"Playback at {percent} ({elapsed} in)";
    }

    private static string DescribeOpenContent(JsonElement attrs)
    {
        var progress = ReadString(attrs, "progress") ?? "0";
        var origin = ReadString(attrs, "Origin");
        return string.IsNullOrEmpty(origin)
            ? $"Opened ebook at {progress}%"
            : $"Opened ebook at {progress}% (from {origin})";
    }

    private static string DescribeLeaveContent(JsonElement attrs, JsonElement metrics)
    {
        var head = $"Closed ebook at {ReadString(attrs, "progress") ?? "0"}%";
        var bits = new List<string>();
        if (ReadLong(metrics, "SecondsRead") is long secs)
            bits.Add($"read {secs}s");
        if (ReadLong(metrics, "PagesTurned") is long pages)
            bits.Add($"{pages} pages turned");
        return bits.Count == 0 ? head : $"{head} — {string.Join(", ", bits)}";
    }

    private static string? ReadString(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;
        if (!obj.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static long? ReadLong(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;
        if (!obj.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : null;
    }

    private static string FormatPercent(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "?";
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return "?";
        return (n * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatElapsed(long? seconds)
    {
        if (seconds is null)
            return "?";
        var s = seconds.Value;
        var hours = s / 3600;
        var minutes = (s % 3600) / 60;
        var secs = s % 60;
        var parts = new List<string>();
        if (hours > 0)
            parts.Add($"{hours}h");
        if (hours > 0 || minutes > 0)
            parts.Add($"{minutes}m");
        parts.Add($"{secs}s");
        return string.Join(" ", parts);
    }

    private sealed record PlaybackInfo
    {
        public string? Description { get; init; }
        public string? ProgressPercent { get; init; }
        public long? SecondsListened { get; init; }
    }
}
