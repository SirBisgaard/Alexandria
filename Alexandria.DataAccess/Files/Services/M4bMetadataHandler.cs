using ATL;
using Alexandria.Business.Library.Models;
using Alexandria.DataAccess.Externals;
using Alexandria.DataAccess.Files.Interfaces;

namespace Alexandria.DataAccess.Files.Services;

public class M4BMetadataHandler(IFfmpegHandler ffmpegHandler) : IM4BMetadataHandler
{
    public Task<Book> ExtractMetadata(string filePath)
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

        return Task.FromResult(book);
    }

    public Task<string?> ExtractCover(string filePath, string coverOutputDirectory, Guid coverUuid)
    {
        var track = new Track(filePath);
        var pic = track.EmbeddedPictures.FirstOrDefault();
        if (pic is null)
            return Task.FromResult<string?>(null);

        var coverPath = Path.Combine(coverOutputDirectory, $"{coverUuid}.jpg");
        File.WriteAllBytes(coverPath, pic.PictureData);

        if (new FileInfo(coverPath).Length == 0)
        {
            File.Delete(coverPath);
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(coverPath);
    }

    public async Task<byte[]> ExtractChapterAsMp3(string sourcePath, AudiobookChapter chapter)
    {
        return await ffmpegHandler.ExtractChapterAsMp3ToMemory(sourcePath, chapter.StartTime, chapter.EndTime);
    }

    public Task EmbedMetadata(string filePath, Book book)
    {
        var track = new Track(filePath);

        if (book.Title is not null)
            track.Title = book.Title;

        if (book.Authors.Count > 0)
        {
            track.Artist = book.Authors[0].Name;
            track.AlbumArtist = book.Authors[0].Name;
        }

        var narrator = book.Contributors.FirstOrDefault(c => c.Role == "Narrator");
        if (narrator is not null)
            track.Composer = narrator.Name;

        if (book.Description is not null)
            track.Comment = book.Description;

        if (book.PublicationDate is not null && int.TryParse(book.PublicationDate, out var year))
            track.Year = year;

        if (book.Publisher is not null)
            track.Publisher = book.Publisher;

        if (!string.IsNullOrEmpty(book.Language))
            track.Language = book.Language;

        if (book.Rights is not null)
            track.Copyright = book.Rights;

        if (book.Subjects.Count > 0)
            track.Genre = book.Subjects[0];

        if (book.Series is not null)
        {
            track.SeriesTitle = book.Series.Name;
            if (book.Series.Index is not null)
                track.SeriesPart = book.Series.Index.Value.ToString();

            track.AdditionalFields["SERIES"] = book.Series.Name;
            if (book.Series.Index is not null)
                track.AdditionalFields["PART"] = book.Series.Index.Value.ToString();

            var grouping = book.Series.Name;
            if (book.Series.Index is not null)
                grouping += $" #{book.Series.Index.Value}";
            track.Group = grouping;
        }

        if (book.BookUuid != Guid.Empty)
            track.AdditionalFields["ALEXANDRIA_UUID"] = book.BookUuid.ToString();

        if (book.Subtitle is not null)
            track.AdditionalFields["SUBTITLE"] = book.Subtitle;

        if (book.IsAbridged is not null)
            track.AdditionalFields["ISABRIDGED"] = book.IsAbridged.Value.ToString().ToLowerInvariant();

        var asin = book.Identifiers.FirstOrDefault(id => id.StartsWith("asin:"));
        if (asin is not null)
            track.AdditionalFields["AUDIBLE_ASIN"] = asin["asin:".Length..];

        track.Save();
        return Task.CompletedTask;
    }

    public Task EmbedCover(string filePath, byte[] coverData, string mediaType)
    {
        var track = new Track(filePath);
        foreach (var pic in track.EmbeddedPictures)
            pic.MarkedForDeletion = true;
        track.EmbeddedPictures.Add(PictureInfo.fromBinaryData(coverData, PictureInfo.PIC_TYPE.Front));
        track.Save();
        return Task.CompletedTask;
    }

    public Task RemoveCover(string filePath)
    {
        var track = new Track(filePath);
        foreach (var pic in track.EmbeddedPictures)
            pic.MarkedForDeletion = true;
        track.Save();
        return Task.CompletedTask;
    }

    private static Series ParseGrouping(string grouping)
    {
        var hashIndex = grouping.LastIndexOf('#');
        if (hashIndex > 0 && decimal.TryParse(grouping[(hashIndex + 1)..].Trim(), out var index))
            return new Series { Name = grouping[..hashIndex].Trim(), Index = index };

        return new Series { Name = grouping };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
