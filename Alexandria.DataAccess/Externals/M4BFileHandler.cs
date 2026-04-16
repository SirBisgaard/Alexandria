using System.Globalization;
using System.Text.Json;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Externals.Models;

namespace Alexandria.DataAccess.Externals;

public class M4BFileHandler(IFfmpegWrapper ffmpegWrapper) : IM4BFileHandler
{
    public async Task<Book> ExtractMetadata(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("M4B file not found.", filePath);

        return await ExtractMetadataViaFfprobe(filePath);
    }

    /// <summary>
    /// Extracts metadata from an M4B file via ffprobe JSON output. Authors are split from the
    /// "artist" or "album_artist" tag by commas. Narrators are mapped from the "composer" tag —
    /// M4B/MP4 has no dedicated narrator field, and "composer" is the de facto convention used
    /// by audiobook tools (Audiobookshelf, mp3tag, etc.). Series is parsed from the "grouping"
    /// tag in "Series Name #N" format, using LastIndexOf('#') to handle names containing '#'
    /// (e.g., "C# Programming"). Multi-series groupings ("A #1; B #2", written by Audiobookshelf
    /// et al.) are collapsed to the first series — our domain and the Kobo sync protocol only
    /// carry one series per book. SERIES/PART custom tags are the fallback.
    /// </summary>
    private async Task<Book> ExtractMetadataViaFfprobe(string filePath)
    {
        var json = await ffmpegWrapper.GetMetadataJson(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var format = root.GetProperty("format");
        var tags = format.TryGetProperty("tags", out var t) ? t : default;

        var book = new Book
        {
            Title = GetTag(tags, "title") ?? Path.GetFileNameWithoutExtension(filePath),
            Description = GetTag(tags, "description") ?? GetTag(tags, "comment"),
            Publisher = GetTag(tags, "publisher") ?? GetTag(tags, "PUBLISHER"),
            Language = GetTag(tags, "LANGUAGE") ?? GetTag(tags, "language") ?? "",
            PublicationDate = GetTag(tags, "date"),
            Rights = GetTag(tags, "copyright"),
            File = new BookFile
            {
                MediaType = MediaType.Audiobook,
                TotalFileSize = new FileInfo(filePath).Length
            }
        };

        if (format.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), CultureInfo.InvariantCulture, out var duration))
            book.File.Duration = (int)Math.Round(duration);

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

        if (root.TryGetProperty("streams", out var streams))
        {
            var audioStream = streams.EnumerateArray().FirstOrDefault(s => s.TryGetProperty("codec_type", out var ct) && ct.GetString() == "audio");
            if (audioStream.ValueKind != JsonValueKind.Undefined)
            {
                book.File.AudioCodec = audioStream.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                if (audioStream.TryGetProperty("bit_rate", out var br) && int.TryParse(br.GetString(), out var bitrate))
                    book.File.AudioBitrate = bitrate;
            }
        }

        return book;
    }

    public Task<byte[]?> ExtractCoverBytes(string filePath) => ffmpegWrapper.ExtractCoverImage(filePath);

    public async Task<string?> ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid)
    {
        var coverData = await ffmpegWrapper.ExtractCoverImage(filePath);
        if (coverData is null)
            return null;

        var coverPath = Path.Combine(coverOutputDirectory, $"{coverUuid}.jpg");
        await File.WriteAllBytesAsync(coverPath, coverData);
        return coverPath;
    }

    public async Task StreamChapter(string sourcePath, AudiobookChapter chapter, string? codec, int? bitrate, Stream outputStream, CancellationToken cancellationToken)
    {
        await ffmpegWrapper.StreamChapter(sourcePath, chapter.StartTime, chapter.EndTime, codec, bitrate, outputStream, cancellationToken);
    }

    /// <summary>
    /// Writes metadata into the M4B file via ffmpeg. Only standard ID3/MP4 tags are written —
    /// non-standard tags (custom TXXX frames, etc.) can cause media players to fail reading
    /// cover art. Uses atomic write via temp file because ffmpeg can't read and write the same file.
    /// Existing chapters are re-extracted and re-embedded to prevent loss during the rewrite.
    /// </summary>
    public async Task EmbedMetadata(string filePath, Book book)
    {
        var existing = await ExtractMetadata(filePath);
        if (existing.MetadataEquals(book))
            return;

        var tags = BuildStandardTagDictionary(book);
        var chapters = BuildChapterList(existing);
        await WriteViaFfmpeg(filePath, (input, output) => ffmpegWrapper.WriteMetadata(input, output, tags, chapters));
    }

    public async Task EmbedCover(string filePath, byte[] coverData, string mediaType)
    {
        var existing = await ExtractMetadata(filePath);
        var tags = BuildStandardTagDictionary(existing);
        var chapters = BuildChapterList(existing);
        await WriteViaFfmpeg(filePath, (input, output) => ffmpegWrapper.WriteMetadataWithCover(input, output, tags, chapters, coverData));
    }

    public async Task RemoveCover(string filePath)
    {
        var existing = await ExtractMetadata(filePath);
        var tags = BuildStandardTagDictionary(existing);
        var chapters = BuildChapterList(existing);
        await WriteViaFfmpeg(filePath, (input, output) => ffmpegWrapper.WriteMetadataRemoveCover(input, output, tags, chapters));
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

    /// <summary>Only standard ID3/MP4 tags are written to preserve cover art compatibility with media players.</summary>
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
        var primary = grouping.Split(';', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(primary))
            return new Series { Name = grouping.Trim() };

        var hashIndex = primary.LastIndexOf('#');
        if (hashIndex > 0 && decimal.TryParse(primary[(hashIndex + 1)..].Trim(), out var index))
            return new Series { Name = primary[..hashIndex].Trim(), Index = index };

        return new Series { Name = primary };
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
