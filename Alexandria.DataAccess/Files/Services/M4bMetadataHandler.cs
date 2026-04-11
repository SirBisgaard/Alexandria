using System.Globalization;
using System.Text.Json;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Externals.Models;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Services;

public class M4BMetadataHandler(IFfmpegHandler ffmpegHandler) : IM4BMetadataHandler
{
    public async Task<Book> ExtractMetadata(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("M4B file not found.", filePath);

        return await ExtractMetadataViaFfprobe(filePath);
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
            Description = GetTag(tags, "description") ?? GetTag(tags, "comment"),
            Publisher = GetTag(tags, "publisher") ?? GetTag(tags, "PUBLISHER"),
            Language = GetTag(tags, "LANGUAGE") ?? GetTag(tags, "language") ?? "",
            PublicationDate = GetTag(tags, "date"),
            Rights = GetTag(tags, "copyright"),
            TotalFileSize = new FileInfo(filePath).Length
        };

        if (format.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), CultureInfo.InvariantCulture, out var duration))
            book.Duration = (int)Math.Round(duration);

        var artists = GetTag(tags, "artist") ?? GetTag(tags, "album_artist");
        if (artists is not null)
            book.Authors = artists.Split(",").Select(artist => new Author { Name = artist.Trim() }).ToList();

        var narrators = GetTag(tags, "composer");
        if (narrators is not null)
            book.Contributors = narrators.Split(",").Select(narrator => new Contributor { Name = narrator.Trim(), Role = "Narrator" }).ToList();

        var grouping = GetTag(tags, "grouping");
        if (grouping is not null)
        {
            book.Series = ParseGrouping(grouping);
        }
        else
        {
            var seriesName = GetTag(tags, "SERIES") ?? GetTag(tags, "series");
            if (seriesName is not null)
            {
                var partStr = GetTag(tags, "PART") ?? GetTag(tags, "part");
                decimal? index = partStr is not null && decimal.TryParse(partStr, out var p) ? p : null;
                book.Series = new Series { Name = seriesName, Index = index };
            }
        }

        var genre = GetTag(tags, "genre");
        if (genre is not null)
            book.Subjects = [genre];

        if (root.TryGetProperty("chapters", out var chapters))
        {
            var chapterIndex = 0;
            foreach (var ch in chapters.EnumerateArray())
            {
                var startTime = double.Parse(ch.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);
                var endTime = double.Parse(ch.GetProperty("end_time").GetString()!, CultureInfo.InvariantCulture);
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

    public Task<byte[]?> ExtractCoverBytes(string filePath) => ffmpegHandler.ExtractCoverImage(filePath);

    public async Task<string?> ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid)
    {
        var coverData = await ffmpegHandler.ExtractCoverImage(filePath);
        if (coverData is null)
            return null;

        var coverPath = Path.Combine(coverOutputDirectory, $"{coverUuid}.jpg");
        await File.WriteAllBytesAsync(coverPath, coverData);
        return coverPath;
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
        await WriteViaFfmpeg(filePath, (input, output) => ffmpegHandler.WriteMetadata(input, output, tags, chapters));
    }

    public async Task EmbedCover(string filePath, byte[] coverData, string mediaType)
    {
        var existing = await ExtractMetadata(filePath);
        var tags = BuildStandardTagDictionary(existing);
        var chapters = BuildChapterList(existing);
        await WriteViaFfmpeg(filePath, (input, output) => ffmpegHandler.WriteMetadataWithCover(input, output, tags, chapters, coverData));
    }

    public async Task RemoveCover(string filePath)
    {
        var existing = await ExtractMetadata(filePath);
        var tags = BuildStandardTagDictionary(existing);
        var chapters = BuildChapterList(existing);
        await WriteViaFfmpeg(filePath, (input, output) => ffmpegHandler.WriteMetadataRemoveCover(input, output, tags, chapters));
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
        {
            tags["title"] = book.Title;
            tags["album"] = book.Subtitle is not null ? $"{book.Title}: {book.Subtitle}" : book.Title;
        }

        if (book.Authors.Count > 0)
        {
            var authorStr = string.Join(", ", book.Authors.Select(a => a.Name));
            tags["artist"] = authorStr;
            tags["album_artist"] = authorStr;
        }

        var narrators = book.Contributors.Where(c => c.Role == "Narrator").ToList();
        if (narrators.Count > 0)
            tags["composer"] = string.Join(", ", narrators.Select(n => n.Name));

        if (book.Description is not null)
        {
            tags["comment"] = book.Description;
            tags["description"] = book.Description;
        }

        if (book.PublicationDate is not null)
            tags["date"] = book.PublicationDate;

        if (book.Rights is not null)
            tags["copyright"] = book.Rights;

        if (book.Publisher is not null)
            tags["publisher"] = book.Publisher;

        if (book.Subjects.Count > 0)
            tags["genre"] = string.Join("; ", book.Subjects);

        if (book.Series is not null)
        {
            var grouping = book.Series.Name;
            if (book.Series.Index is not null)
                grouping += $" #{book.Series.Index.Value}";
            tags["grouping"] = grouping;
        }

        return tags;
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
}
