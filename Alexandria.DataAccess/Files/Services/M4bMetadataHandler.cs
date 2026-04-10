using System.Globalization;
using System.Text.Json;
using ATL;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Interfaces;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Files.Services;

public class M4BMetadataHandler(IFfmpegHandler ffmpegHandler, ILogger<M4BMetadataHandler> logger) : IM4BMetadataHandler
{
    public async Task<Book> ExtractMetadata(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        try
        {
            var book = ExtractMetadataViaAtl(filePath);
            if (book.Authors.Count > 0 || book.Title != fileName)
                return book;

            logger.LogWarning("ATL returned incomplete metadata for {FileName}, falling back to ffprobe", Path.GetFileName(filePath));
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            logger.LogWarning(ex, "ATL failed for {FileName}, falling back to ffprobe", Path.GetFileName(filePath));
        }

        return await ExtractMetadataViaFfprobe(filePath);
    }

    private Book ExtractMetadataViaAtl(string filePath)
    {
        var track = new Track(filePath);

        var book = new Book
        {
            MediaType = MediaType.Audiobook,
            Title = NullIfEmpty(track.Title) ?? Path.GetFileNameWithoutExtension(filePath),
            Subtitle = GetAdditionalField(track, "SUBTITLE") ?? GetAdditionalField(track, "subtitle"),
            Description = NullIfEmpty(track.Comment) ?? NullIfEmpty(track.Description),
            Publisher = NullIfEmpty(track.Publisher),
            Language = GetAdditionalField(track, "LANGUAGE") ?? GetAdditionalField(track, "language") ?? NullIfEmpty(track.Language) ?? "",
            PublicationDate = track.Year > 0 ? track.Year.ToString() : null,
            Rights = NullIfEmpty(track.Copyright),
            Duration = (int)Math.Round((double)track.Duration),
            TotalFileSize = new FileInfo(filePath).Length
        };

        var alexandriaUuid = GetAdditionalField(track, "ALEXANDRIA_UUID");
        if (alexandriaUuid is not null && Guid.TryParse(alexandriaUuid, out var parsedUuid) && parsedUuid != Guid.Empty)
            book.BookUuid = parsedUuid;

        var artist = NullIfEmpty(track.Artist) ?? NullIfEmpty(track.AlbumArtist);
        if (artist is not null)
            book.Authors = [new Author { Name = artist }];

        var narrator = NullIfEmpty(track.Composer);
        if (narrator is not null)
            book.Contributors = [new Contributor { Name = narrator, Role = "Narrator" }];

        var seriesName = NullIfEmpty(track.SeriesTitle) ?? GetAdditionalField(track, "SERIES") ?? GetAdditionalField(track, "series");
        if (seriesName is not null)
        {
            var partStr = NullIfEmpty(track.SeriesPart) ?? GetAdditionalField(track, "PART") ?? GetAdditionalField(track, "part");
            decimal? index = partStr is not null && decimal.TryParse(partStr, out var p) ? p : null;
            book.Series = new Series { Name = seriesName, Index = index };
        }
        else
        {
            var grouping = NullIfEmpty(track.Group) ?? GetAdditionalField(track, "grouping");
            if (grouping is not null)
                book.Series = ParseGrouping(grouping);
        }

        var isAbridged = GetAdditionalField(track, "ISABRIDGED") ?? GetAdditionalField(track, "isAbridged");
        if (isAbridged is not null && bool.TryParse(isAbridged, out var abridged))
            book.IsAbridged = abridged;

        var asin = GetAdditionalField(track, "AUDIBLE_ASIN") ?? GetAdditionalField(track, "asin") ?? GetAdditionalField(track, "ASIN");
        if (asin is not null)
            book.Identifiers = [$"asin:{asin}"];

        var genre = NullIfEmpty(track.Genre);
        if (genre is not null)
            book.Subjects = [genre];

        var chapterIndex = 0;
        foreach (var ch in track.Chapters)
        {
            var startTime = ch.StartTime / 1000.0;
            var endTime = ch.EndTime / 1000.0;
            book.Chapters.Add(new AudiobookChapter
            {
                ChapterIndex = chapterIndex,
                Title = (NullIfEmpty(ch.Title) ?? $"Chapter {chapterIndex + 1}").Trim(),
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            });
            chapterIndex++;
        }

        return book;
    }

    private async Task<Book> ExtractMetadataViaFfprobe(string filePath)
    {
        var json = await ffmpegHandler.GetMetadataJson(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var format = root.GetProperty("format");
        var tags = format.TryGetProperty("tags", out var t) ? t : default;

        var book = new Book
        {
            MediaType = MediaType.Audiobook,
            Title = GetTag(tags, "title") ?? Path.GetFileNameWithoutExtension(filePath),
            Subtitle = GetTag(tags, "SUBTITLE") ?? GetTag(tags, "subtitle"),
            Description = GetTag(tags, "comment") ?? GetTag(tags, "description"),
            Publisher = GetTag(tags, "PUBLISHER") ?? GetTag(tags, "publisher"),
            Language = GetTag(tags, "LANGUAGE") ?? GetTag(tags, "language") ?? "",
            PublicationDate = GetTag(tags, "date"),
            Rights = GetTag(tags, "copyright"),
            TotalFileSize = new FileInfo(filePath).Length
        };

        if (format.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var duration))
            book.Duration = (int)Math.Round(duration);

        var alexandriaUuid = GetTag(tags, "ALEXANDRIA_UUID");
        if (alexandriaUuid is not null && Guid.TryParse(alexandriaUuid, out var parsedUuid) && parsedUuid != Guid.Empty)
            book.BookUuid = parsedUuid;

        var artist = GetTag(tags, "artist") ?? GetTag(tags, "album_artist");
        if (artist is not null)
            book.Authors = [new Author { Name = artist }];

        var narrator = GetTag(tags, "composer");
        if (narrator is not null)
            book.Contributors = [new Contributor { Name = narrator, Role = "Narrator" }];

        var seriesName = GetTag(tags, "SERIES") ?? GetTag(tags, "series");
        if (seriesName is not null)
        {
            var partStr = GetTag(tags, "PART") ?? GetTag(tags, "part");
            decimal? index = partStr is not null && decimal.TryParse(partStr, out var p) ? p : null;
            book.Series = new Series { Name = seriesName, Index = index };
        }
        else
        {
            var grouping = GetTag(tags, "grouping");
            if (grouping is not null)
                book.Series = ParseGrouping(grouping);
        }

        var isAbridged = GetTag(tags, "ISABRIDGED") ?? GetTag(tags, "isAbridged");
        if (isAbridged is not null && bool.TryParse(isAbridged, out var abridged))
            book.IsAbridged = abridged;

        var asin = GetTag(tags, "AUDIBLE_ASIN") ?? GetTag(tags, "asin") ?? GetTag(tags, "ASIN");
        if (asin is not null)
            book.Identifiers = [$"asin:{asin}"];

        var genre = GetTag(tags, "genre");
        if (genre is not null)
            book.Subjects = [genre];

        if (root.TryGetProperty("chapters", out var chapters))
        {
            var chapterIndex = 0;
            foreach (var ch in chapters.EnumerateArray())
            {
                var startTime = double.Parse(ch.GetProperty("start_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var endTime = double.Parse(ch.GetProperty("end_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var chTitle = ch.TryGetProperty("tags", out var chTags) ? GetTag(chTags, "title") : null;

                book.Chapters.Add(new AudiobookChapter
                {
                    ChapterIndex = chapterIndex,
                    Title = (chTitle ?? $"Chapter {chapterIndex + 1}").Trim(),
                    Duration = endTime - startTime,
                    StartTime = startTime,
                    EndTime = endTime
                });
                chapterIndex++;
            }
        }

        return book;
    }

    public async Task<string?> ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid)
    {
        try
        {
            var track = new Track(filePath);
            var pic = track.EmbeddedPictures.FirstOrDefault();
            if (pic is not null)
            {
                var coverPath = Path.Combine(coverOutputDirectory, $"{coverUuid}.jpg");
                File.WriteAllBytes(coverPath, pic.PictureData);

                if (new FileInfo(coverPath).Length > 0)
                    return coverPath;

                File.Delete(coverPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ATL cover extraction failed for {FileName}, falling back to ffmpeg", Path.GetFileName(filePath));
        }

        var coverData = await ffmpegHandler.ExtractCoverImage(filePath);
        if (coverData is null)
            return null;

        var fallbackPath = Path.Combine(coverOutputDirectory, $"{coverUuid}.jpg");
        await File.WriteAllBytesAsync(fallbackPath, coverData);
        return fallbackPath;
    }

    public async Task<byte[]> ExtractChapterAsMp3(string sourcePath, AudiobookChapter chapter)
    {
        return await ffmpegHandler.ExtractChapterAsMp3ToMemory(sourcePath, chapter.StartTime, chapter.EndTime);
    }

    public async Task EmbedMetadata(string filePath, Book book)
    {
        var existing = await ExtractMetadata(filePath);
        if (existing.MetadataEquals(book))
            return;

        var tags = BuildStandardTagDictionary(book);
        var chapters = BuildChapterList(existing);
        var coverData = await ffmpegHandler.ExtractCoverImage(filePath);

        if (coverData is not null)
            await WriteViaFfmpeg(filePath, (input, output) => ffmpegHandler.WriteMetadataWithCover(input, output, tags, chapters, coverData));
        else
            await WriteViaFfmpeg(filePath, (input, output) => ffmpegHandler.WriteMetadata(input, output, tags, chapters));

        WriteCustomTagsViaAtl(filePath, book, existing);
    }

    public async Task EmbedCover(string filePath, byte[] coverData, string mediaType)
    {
        var existing = await ExtractMetadata(filePath);
        var tags = BuildStandardTagDictionary(existing);
        var chapters = BuildChapterList(existing);
        await WriteViaFfmpeg(filePath, (input, output) => ffmpegHandler.WriteMetadataWithCover(input, output, tags, chapters, coverData));
        WriteCustomTagsViaAtl(filePath, existing, existing);
    }

    public async Task RemoveCover(string filePath)
    {
        var existing = await ExtractMetadata(filePath);
        var tags = BuildStandardTagDictionary(existing);
        var chapters = BuildChapterList(existing);
        await WriteViaFfmpeg(filePath, (input, output) => ffmpegHandler.WriteMetadataRemoveCover(input, output, tags, chapters));
        WriteCustomTagsViaAtl(filePath, existing, existing);
    }

    private static async Task WriteViaFfmpeg(string filePath, Func<string, string, Task> action)
    {
        var tempPath = filePath + ".tmp";
        try
        {
            await action(filePath, tempPath);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static Dictionary<string, string> BuildStandardTagDictionary(Book book)
    {
        var tags = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(book.Title))
            tags["title"] = book.Title;

        if (book.Authors.Count > 0)
        {
            tags["artist"] = book.Authors[0].Name;
            tags["album_artist"] = book.Authors[0].Name;
        }

        var narrator = book.Contributors.FirstOrDefault(c => c.Role == "Narrator");
        if (narrator is not null)
            tags["composer"] = narrator.Name;

        if (book.Description is not null)
            tags["comment"] = book.Description;

        if (book.PublicationDate is not null)
            tags["date"] = book.PublicationDate;

        if (book.Rights is not null)
            tags["copyright"] = book.Rights;

        if (book.Subjects.Count > 0)
            tags["genre"] = book.Subjects[0];

        if (book.Series is not null)
        {
            var grouping = book.Series.Name;
            if (book.Series.Index is not null)
                grouping += $" #{book.Series.Index.Value}";
            tags["grouping"] = grouping;
        }

        return tags;
    }

    private static void WriteCustomTagsViaAtl(string filePath, Book book, Book existing)
    {
        var track = new Track(filePath);

        if (!string.IsNullOrEmpty(book.Language))
            track.AdditionalFields["LANGUAGE"] = book.Language;

        if (book.Publisher is not null)
            track.Publisher = book.Publisher;

        if (book.Series is not null)
        {
            track.AdditionalFields["SERIES"] = book.Series.Name;
            if (book.Series.Index is not null)
                track.AdditionalFields["PART"] = book.Series.Index.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (existing.BookUuid == Guid.Empty && book.BookUuid != Guid.Empty)
            track.AdditionalFields["ALEXANDRIA_UUID"] = book.BookUuid.ToString();
        else if (existing.BookUuid != Guid.Empty)
            track.AdditionalFields["ALEXANDRIA_UUID"] = existing.BookUuid.ToString();

        if (book.Subtitle is not null)
            track.AdditionalFields["SUBTITLE"] = book.Subtitle;

        if (book.IsAbridged is not null)
            track.AdditionalFields["ISABRIDGED"] = book.IsAbridged.Value.ToString().ToLowerInvariant();

        var asin = book.Identifiers.FirstOrDefault(id => id.StartsWith("asin:"));
        if (asin is not null)
            track.AdditionalFields["AUDIBLE_ASIN"] = asin["asin:".Length..];

        track.Save();
    }

    private static List<FfmpegChapter> BuildChapterList(Book book)
    {
        return book.Chapters.Select(c => new FfmpegChapter((long)Math.Round(c.StartTime * 1000), (long)Math.Round(c.EndTime * 1000), c.Title)).ToList();
    }

    private static Series ParseGrouping(string grouping)
    {
        var hashIndex = grouping.LastIndexOf('#');
        if (hashIndex > 0 && decimal.TryParse(grouping[(hashIndex + 1)..].Trim(), out var index))
            return new Series { Name = grouping[..hashIndex].Trim(), Index = index };

        return new Series { Name = grouping };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? GetTag(JsonElement tags, string key)
    {
        if (tags.ValueKind == JsonValueKind.Undefined)
            return null;

        if (tags.TryGetProperty(key, out var value))
        {
            var str = value.GetString();
            return string.IsNullOrWhiteSpace(str) ? null : str;
        }

        return null;
    }

    private static string? GetAdditionalField(Track track, string key)
    {
        if (track.AdditionalFields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        foreach (var kv in track.AdditionalFields)
        {
            if (kv.Key.EndsWith($":{key}", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                return kv.Value;
        }

        return null;
    }
}
