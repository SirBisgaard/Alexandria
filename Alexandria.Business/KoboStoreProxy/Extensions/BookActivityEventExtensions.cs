using System.Globalization;
using System.Text.Json;
using Alexandria.Business.KoboStoreProxy.Models;

namespace Alexandria.Business.KoboStoreProxy.Extensions;

public static class BookActivityEventExtensions
{
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

    private static string DescribePlayback(JsonElement attrs, JsonElement metrics)
    {
        var description = ReadString(attrs, "Description");
        var percent = FormatPercent(ReadString(attrs, "ProgressPercent"));
        var elapsed = FormatElapsed(ReadLong(metrics, "ProgressTime"));
        if (description == "Start")
            return $"Started at {percent} ({elapsed} in)";
        if (description == "Pause")
        {
            var listened = ReadString(attrs, "SecondsListened");
            var suffix = !string.IsNullOrEmpty(listened) ? $" — listened {listened}s" : "";
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
}
