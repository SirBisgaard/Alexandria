using System.Globalization;
using System.Text.Json;
using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals.Interfaces;
using Alexandria.DataAccess.Externals.Models;

namespace Alexandria.DataAccess.Externals;

public class Mp3FileHandler(IFfmpegWrapper ffmpegWrapper) : IMp3FileHandler
{
    /// <summary>
    /// Extracts metadata by probing each MP3 file once. Album-level tags come from the first
    /// file; per-file duration and title build the chapter list. This is the only method that
    /// calls ffprobe — all other methods use filename sort (zero overhead).
    /// </summary>
    public async Task<Book> ExtractMetadata(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"MP3 audiobook folder not found: {folderPath}");

        var files = await GetSortedMp3Files(folderPath);
        if (files.Length == 0)
            throw new FileNotFoundException("No MP3 files found in folder.", folderPath);

        var firstJson = await ffmpegWrapper.GetMetadataJson(files[0]);
        using var firstDoc = JsonDocument.Parse(firstJson);
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
            var fileJson = i == 0 ? firstJson : await ffmpegWrapper.GetMetadataJson(files[i]);
            using var doc = i == 0 ? null : JsonDocument.Parse(fileJson);
            var root = i == 0 ? firstRoot : doc!.RootElement;
            var fmt = root.GetProperty("format");
            var fileTags = fmt.TryGetProperty("tags", out var ft) ? ft : default;

            var duration = fmt.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), CultureInfo.InvariantCulture, out var d) ? d : 0;
            var chapterTitle = GetTag(fileTags, "title") ?? Path.GetFileNameWithoutExtension(files[i]);

            book.Chapters.Add(new AudiobookChapter
            {
                ChapterIndex = i,
                Title = chapterTitle.Trim(),
                Duration = duration,
                StartTime = cumulativeTime,
                EndTime = cumulativeTime + duration,
                FileName = Path.GetFileName(files[i])
            });

            cumulativeTime += duration;
            totalSize += new FileInfo(files[i]).Length;
        }

        book.File.Duration = (int)Math.Round(cumulativeTime);
        book.File.TotalFileSize = totalSize;

        return book;
    }

    public async Task<byte[]?> ExtractCoverBytes(string folderPath)
    {
        var coverFile = GetFolderCoverPath(folderPath);
        if (File.Exists(coverFile))
            return await File.ReadAllBytesAsync(coverFile);

        var files = await GetSortedMp3Files(folderPath);
        if (files.Length == 0)
            return null;
        return await ffmpegWrapper.ExtractCoverImage(files[0]);
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

    public async Task EmbedMetadata(Book book)
    {
        var folderPath = ToFolderPath(book.File.SourcePath);
        var existing = await ExtractMetadata(folderPath);
        if (existing.MetadataEquals(book))
            return;

        var files = await GetSortedMp3Files(folderPath);
        var albumTags = BuildStandardTagDictionary(book);
        var total = files.Length;

        for (var i = 0; i < files.Length; i++)
        {
            var perFileTags = new Dictionary<string, string>(albumTags)
            {
                ["title"] = i < book.Chapters.Count ? book.Chapters[i].Title : Path.GetFileNameWithoutExtension(files[i]),
                ["track"] = $"{i + 1}/{total}"
            };
            await WriteViaFfmpeg(files[i], (input, output) => ffmpegWrapper.WriteMetadata(input, output, perFileTags, []));
        }
    }

    public async Task EmbedCover(Book book, byte[] coverData, string mediaType)
    {
        var folderPath = ToFolderPath(book.File.SourcePath);
        await File.WriteAllBytesAsync(GetFolderCoverPath(folderPath), coverData);

        var files = await GetSortedMp3Files(folderPath);
        foreach (var file in files)
            await WriteViaFfmpeg(file, (input, output) => ffmpegWrapper.WriteMetadataWithCover(input, output, ReadExistingTags(file), [], coverData));
    }

    public async Task RemoveCover(Book book)
    {
        var folderPath = ToFolderPath(book.File.SourcePath);
        var coverFile = GetFolderCoverPath(folderPath);
        if (File.Exists(coverFile))
            File.Delete(coverFile);

        var files = await GetSortedMp3Files(folderPath);
        foreach (var file in files)
            await WriteViaFfmpeg(file, (input, output) => ffmpegWrapper.WriteMetadataRemoveCover(input, output, ReadExistingTags(file), []));
    }

    /// <summary>
    /// Returns MP3 files sorted by ID3 track number, falling back to filename sort.
    /// </summary>
    private async Task<string[]> GetSortedMp3Files(string folderPath)
    {
        var mp3Files = Directory.GetFiles(folderPath, "*.mp3");
        if (mp3Files.Length == 0)
            return [];

        var fileTrackPairs = new List<(string Path, int Track, string FileName)>();

        foreach (var file in mp3Files)
        {
            var track = int.MaxValue;
            try
            {
                var json = await ffmpegWrapper.GetMetadataJson(file);
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
                // Fall back to filename sort
            }
            fileTrackPairs.Add((file, track, Path.GetFileName(file)));
        }

        return fileTrackPairs
            .OrderBy(f => f.Track)
            .ThenBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.Path)
            .ToArray();
    }

    /// <summary>Reads existing tags from a single MP3 file via ffprobe, filtered to standard keys.</summary>
    private Dictionary<string, string> ReadExistingTags(string filePath)
    {
        var json = ffmpegWrapper.GetMetadataJson(filePath).GetAwaiter().GetResult();
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

    private static Dictionary<string, string> BuildStandardTagDictionary(Book book)
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
