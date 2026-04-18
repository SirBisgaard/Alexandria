using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Externals;

public class Mp3FileHandler(IFfmpegWrapper ffmpegWrapper, ICoverThumbnailService coverThumbnailService) : IMp3FileHandler
{
    /// <summary>
    /// Extracts metadata by probing each MP3 file once. Album-level tags come from the first
    /// file; per-file duration and title build the chapter list. The sort phase and the
    /// chapter read phase share a single ffprobe per file via <see cref="ProbeAndSort"/> —
    /// earlier versions did 2×N probes because sort and read each spawned ffprobe separately.
    /// </summary>
    public async Task<Book> ExtractMetadata(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"MP3 audiobook folder not found: {folderPath}");

        var files = await ProbeAndSort(folderPath);
        if (files.Length == 0)
            throw new FileNotFoundException("No MP3 files found in folder.", folderPath);

        return BuildBookFromProbed(files, folderPath);
    }

    /// <summary>
    /// Single-pass entry point for indexing: probes each MP3 once, builds the <see cref="Book"/>,
    /// and returns the cover bytes (from <c>cover.jpg</c> when present, otherwise extracted from
    /// the first file's embedded artwork). Replaces the old flow that invoked
    /// <see cref="ExtractMetadata"/> and a separate <c>ExtractCoverBytes</c> — each of which did
    /// its own ProbeAndSort, doubling the work when no <c>cover.jpg</c> existed.
    /// </summary>
    public async Task<(Book Metadata, byte[]? CoverBytes)> Extract(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"MP3 audiobook folder not found: {folderPath}");

        var files = await ProbeAndSort(folderPath);
        if (files.Length == 0)
            throw new FileNotFoundException("No MP3 files found in folder.", folderPath);

        var book = BuildBookFromProbed(files, folderPath);

        var coverFile = GetFolderCoverPath(folderPath);
        byte[]? cover = File.Exists(coverFile)
            ? await File.ReadAllBytesAsync(coverFile)
            : await ffmpegWrapper.ExtractCoverImage(files[0].Path);

        return (book, cover);
    }

    private Book BuildBookFromProbed((string Path, string Json)[] files, string folderPath)
    {
        using var firstDoc = JsonDocument.Parse(files[0].Json);
        var firstRoot = firstDoc.RootElement;
        var format = firstRoot.GetProperty("format");
        var tags = format.TryGetProperty("tags", out var t) ? t : default;

        var book = new Book
        {
            Title = GetTag(tags, "album") ?? Path.GetFileName(folderPath),
            Description = GetTag(tags, "description") ?? GetTag(tags, "comment"),
            Publisher = GetTag(tags, "publisher") ?? GetTag(tags, "PUBLISHER"),
            Language = GetTag(tags, "LANGUAGE") ?? GetTag(tags, "language") ?? "",
            PublicationDate = GetTag(tags, "date"),
            Rights = GetTag(tags, "copyright"),
            File = new BookFile { MediaType = MediaType.Audiobook, AudioCodec = "mp3" }
        };

        var artists = GetTag(tags, "artist") ?? GetTag(tags, "album_artist");
        if (artists is not null)
            book.Authors = artists.Split(",").Select(a => new Author { Name = a.Trim() }).ToList();

        var narrators = GetTag(tags, "composer");
        if (narrators is not null)
            book.Contributors = narrators.Split(",").Select(n => new Contributor { Name = n.Trim(), Role = "Narrator" }).ToList();

        var grouping = GetTag(tags, "grouping");
        if (grouping is not null)
        {
            book.Series = ParseGrouping(grouping);
        }
        else
        {
            // SERIES/PART fallback for third-party-produced files (Audiobookshelf, mp3tag, etc.).
            // Once the user edits metadata through Alexandria, ffmpeg's ID3v2 muxer drops these
            // non-standard tag names on write, so this branch never fires on files we've re-muxed
            // — by then the user has explicitly set a Series via the UI anyway.
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

        if (firstRoot.TryGetProperty("streams", out var streams))
        {
            var audioStream = streams.EnumerateArray().FirstOrDefault(s => s.TryGetProperty("codec_type", out var ct) && ct.GetString() == "audio");
            if (audioStream.ValueKind != JsonValueKind.Undefined && audioStream.TryGetProperty("bit_rate", out var br) && int.TryParse(br.GetString(), out var bitrate))
                book.File.AudioBitrate = bitrate;
        }

        long totalSize = 0;
        double cumulativeTime = 0;

        for (var i = 0; i < files.Length; i++)
        {
            using var doc = i == 0 ? null : JsonDocument.Parse(files[i].Json);
            var root = i == 0 ? firstRoot : doc!.RootElement;
            var fmt = root.GetProperty("format");
            var fileTags = fmt.TryGetProperty("tags", out var ft) ? ft : default;

            var duration = fmt.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), CultureInfo.InvariantCulture, out var d) ? d : 0;
            var chapterTitle = GetTag(fileTags, "title") ?? Path.GetFileNameWithoutExtension(files[i].Path);

            book.Chapters.Add(new AudiobookChapter
            {
                ChapterIndex = i,
                Title = chapterTitle.Trim(),
                Duration = duration,
                StartTime = cumulativeTime,
                EndTime = cumulativeTime + duration,
                FileName = Path.GetFileName(files[i].Path)
            });

            cumulativeTime += duration;
            totalSize += new FileInfo(files[i].Path).Length;
        }

        book.File.Duration = (int)Math.Round(cumulativeTime);
        book.File.TotalFileSize = totalSize;

        return book;
    }

    /// <summary>
    /// Returns <c>cover.jpg</c> if present, otherwise the embedded album art from the first MP3
    /// (filename-sorted — no ffprobe). Audiobook chapters carry identical embedded covers, so
    /// the "first by filename" choice is byte-equivalent to any other sort.
    /// </summary>
    public async Task<byte[]?> ExtractCoverBytes(string folderPath)
    {
        var coverFile = GetFolderCoverPath(folderPath);
        if (File.Exists(coverFile))
            return await File.ReadAllBytesAsync(coverFile);

        var mp3Files = Directory.GetFiles(folderPath, "*.mp3");
        if (mp3Files.Length == 0)
            return null;
        Array.Sort(mp3Files, StringComparer.OrdinalIgnoreCase);
        return await ffmpegWrapper.ExtractCoverImage(mp3Files[0]);
    }

    public async Task<string?> ExtractCover(string folderPath, string coverOutputDirectory, Guid coverUuid)
    {
        var coverData = await ExtractCoverBytes(folderPath);
        if (coverData is null)
            return null;

        var coverPath = Path.Combine(coverOutputDirectory, $"{coverUuid}.jpg");
        await File.WriteAllBytesAsync(coverPath, coverData);
        return coverPath;
    }

    /// <summary>
    /// Streams the raw MP3 file for the given chapter directly to the output stream.
    /// Uses the persisted FileName — no ffprobe, no sorting, just a direct file read.
    /// </summary>
    public async Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken)
    {
        var chapter = book.Chapters[chapterIndex];
        if (chapter.FileName is null)
            throw new InvalidOperationException("Chapter has no FileName — cannot resolve MP3 file.");

        var filePath = Path.Combine(ToFolderPath(book.File.SourcePath), chapter.FileName);
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await fileStream.CopyToAsync(outputStream, cancellationToken);
    }

    /// <summary>
    /// Writes album/title/track tags to every MP3 in the book's folder. When <paramref name="coverBytes"/>
    /// is provided, the bytes are normalized to JPEG (re-encoded via <see cref="ICoverThumbnailService"/>
    /// if not already JPEG), <c>cover.jpg</c> is written to the folder, and the cover is embedded
    /// alongside the tags in a single ffmpeg pass per file; the <see cref="BookExtensions.MetadataEquals"/>
    /// short-circuit is skipped because the caller explicitly asked for a cover write.
    /// </summary>
    public async Task EmbedMetadata(Book book, byte[]? coverBytes = null)
    {
        var folderPath = ToFolderPath(book.File.SourcePath);
        var jpegCover = coverBytes is null ? null : NormalizeToJpeg(coverBytes);
        var files = await ProbeAndSort(folderPath);

        if (jpegCover is null)
        {
            var existing = BuildBookFromProbed(files, folderPath);
            if (existing.MetadataEquals(book))
                return;
        }
        else
        {
            await File.WriteAllBytesAsync(GetFolderCoverPath(folderPath), jpegCover);
        }

        var albumTags = BuildStandardTagDictionary(book);
        var total = files.Length;
        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismPolicy.MaxDegree };

        await Parallel.ForEachAsync(Enumerable.Range(0, total), options, async (i, ct) =>
        {
            var perFileTags = new Dictionary<string, string>(albumTags)
            {
                ["title"] = i < book.Chapters.Count ? book.Chapters[i].Title : Path.GetFileNameWithoutExtension(files[i].Path),
                ["track"] = $"{i + 1}/{total}"
            };
            if (jpegCover is null)
                await WriteViaFfmpeg(files[i].Path, (input, output) => ffmpegWrapper.WriteMetadata(input, output, perFileTags, []));
            else
                await WriteViaFfmpeg(files[i].Path, (input, output) => ffmpegWrapper.WriteMetadataWithCover(input, output, perFileTags, [], jpegCover));
        });
    }

    /// <summary>
    /// Embeds the cover (normalized to JPEG) into every MP3 in the book's folder and writes
    /// <c>cover.jpg</c> alongside them. Existing per-file tags are preserved.
    /// </summary>
    public async Task EmbedCover(Book book, byte[] coverData)
    {
        var jpegCover = NormalizeToJpeg(coverData);
        var folderPath = ToFolderPath(book.File.SourcePath);
        await File.WriteAllBytesAsync(GetFolderCoverPath(folderPath), jpegCover);

        var files = await ProbeAndSort(folderPath);
        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismPolicy.MaxDegree };
        await Parallel.ForEachAsync(files, options, async (file, ct) =>
        {
            var existingTags = ParseStandardTagsFromJson(file.Json);
            await WriteViaFfmpeg(file.Path, (input, output) => ffmpegWrapper.WriteMetadataWithCover(input, output, existingTags, [], jpegCover));
        });
    }

    private byte[] NormalizeToJpeg(byte[] data) => IsJpeg(data) ? data : coverThumbnailService.ConvertToJpeg(data);

    private static bool IsJpeg(byte[] data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    public async Task RemoveCover(Book book)
    {
        var folderPath = ToFolderPath(book.File.SourcePath);
        var coverFile = GetFolderCoverPath(folderPath);
        if (File.Exists(coverFile))
            File.Delete(coverFile);

        var files = await ProbeAndSort(folderPath);
        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismPolicy.MaxDegree };
        await Parallel.ForEachAsync(files, options, async (file, ct) =>
        {
            var existingTags = ParseStandardTagsFromJson(file.Json);
            await WriteViaFfmpeg(file.Path, (input, output) => ffmpegWrapper.WriteMetadataRemoveCover(input, output, existingTags, []));
        });
    }

    /// <summary>
    /// Probes every <c>.mp3</c> once, parses out the ID3 track number for sort, and returns
    /// the files in play order paired with the raw ffprobe JSON. Callers that need full
    /// per-file metadata (durations, chapter titles, streams) reuse the cached JSON instead
    /// of re-probing, collapsing the old 2×N ffprobe pattern to N.
    /// </summary>
    private async Task<(string Path, string Json)[]> ProbeAndSort(string folderPath)
    {
        var mp3Files = Directory.GetFiles(folderPath, "*.mp3");
        if (mp3Files.Length == 0)
            return [];

        var fileRecords = new ConcurrentBag<(string Path, string Json, int Track, string FileName)>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismPolicy.MaxDegree };

        await Parallel.ForEachAsync(mp3Files, options, async (file, ct) =>
        {
            var track = int.MaxValue;
            var json = "";
            try
            {
                json = await ffmpegWrapper.GetMetadataJson(file);
                using var doc = JsonDocument.Parse(json);
                var fmt = doc.RootElement.GetProperty("format");
                if (fmt.TryGetProperty("tags", out var tags))
                {
                    var trackStr = GetTag(tags, "track");
                    if (trackStr is not null)
                    {
                        var trackPart = trackStr.Split('/')[0];
                        if (int.TryParse(trackPart, out var t))
                            track = t;
                    }
                }
            }
            catch
            {
                // Fall back to filename sort; json stays empty and the main-loop parse will throw clearly if used.
            }
            fileRecords.Add((file, json, track, Path.GetFileName(file)));
        });

        return fileRecords
            .OrderBy(f => f.Track)
            .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(f => (f.Path, f.Json))
            .ToArray();
    }

    /// <summary>
    /// Parses standard ID3 tag keys out of an already-fetched ffprobe JSON string. Callers feed
    /// the cached JSON from <see cref="ProbeAndSort"/>, so no additional ffprobe spawns are
    /// needed to read "existing tags" during write operations.
    /// </summary>
    private static Dictionary<string, string> ParseStandardTagsFromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return [];
        using var doc = JsonDocument.Parse(json);
        var format = doc.RootElement.GetProperty("format");
        if (!format.TryGetProperty("tags", out var tags))
            return [];

        var result = new Dictionary<string, string>();
        string[] standardKeys = ["title", "album", "artist", "album_artist", "composer", "comment", "description", "date", "copyright", "publisher", "genre", "grouping", "track"];
        foreach (var key in standardKeys)
        {
            var val = GetTag(tags, key);
            if (val is not null)
                result[key] = val;
        }
        return result;
    }

    public static Dictionary<string, string> BuildStandardTagDictionary(Book book)
    {
        var tags = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(book.Title))
            tags["album"] = book.Subtitle is not null ? $"{book.Title}: {book.Subtitle}" : book.Title;

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

    private static string ToFolderPath(string sourcePath) => sourcePath.EndsWith(".mp3audiobook") ? sourcePath[..^".mp3audiobook".Length] : sourcePath;

    private static string GetFolderCoverPath(string folderPath) => Path.Combine(folderPath, "cover.jpg");

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
