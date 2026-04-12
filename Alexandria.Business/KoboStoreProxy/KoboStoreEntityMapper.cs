using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;

namespace Alexandria.Business.KoboStoreProxy;

/// <summary>
/// Maps Alexandria Book models to Kobo store protocol entities. The Kobo device expects specific
/// JSON shapes, timestamp formats, and property names — deviations cause silent sync failures.
/// </summary>
public static class KoboStoreEntityMapper
{
    private static string ToKoboTimestamp(DateTimeOffset dateTime) => dateTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    /// <summary>
    /// Creates a Kobo entitlement record. CrossRevisionId, Id, and RevisionId are all set to
    /// EntitlementUuid — Kobo uses these for deduplication and they must be consistent.
    /// OriginCategory is "Purchase" for audiobooks (routes to audiobook player) or "Imported"
    /// for ebooks (routes to standard reader). Using the wrong category breaks playback.
    /// </summary>
    public static KoboStoreBookEntitlement ToEntitlement(Book book) => new()
    {
        ActivePeriod = new KoboStoreActivePeriod { From = ToKoboTimestamp(book.CreatedAt) },
        Created = ToKoboTimestamp(book.CreatedAt),
        CrossRevisionId = book.EntitlementUuid.ToString(),
        Id = book.EntitlementUuid.ToString(),
        IsRemoved = false,
        LastModified = ToKoboTimestamp(book.ModifiedAt),
        OriginCategory = book.MediaType == MediaType.Audiobook ? "Purchase" : "Imported",
        RevisionId = book.EntitlementUuid.ToString()
    };

    /// <summary>
    /// Creates Kobo metadata for a book. Download format is "MANIFEST" for audiobooks (two-step:
    /// manifest then chapters on demand) or "KEPUB" for ebooks (single-file download).
    /// CoverImageId uses "{uuid}--v{unix seconds}" format so cover URL changes when metadata/cover
    /// is updated — the Kobo device caches covers by URL and won't re-fetch without a URL change.
    /// Series index uses "G29" format (no trailing zeros: 1.0→"1", 2.5→"2.5") to match Kobo's display.
    /// Timestamps must be ISO 8601 with "Z" suffix — Kobo rejects "+00:00" format.
    /// </summary>
    public static KoboStoreBookMetadata ToMetadata(Book book, string baseUrl, Guid? relatedGroupId = null)
    {
        var entitlementUuid = book.EntitlementUuid.ToString();

        var (format, downloadUrl) = book.MediaType switch
        {
            MediaType.Audiobook => ("MANIFEST", $"{baseUrl}/download/{entitlementUuid}/manifest"),
            _ => ("KEPUB", $"{baseUrl}/download/{entitlementUuid}/kepub")
        };

        var metadata = new KoboStoreBookMetadata
        {
            CoverImageId = $"{entitlementUuid}--v{book.ModifiedAt.ToUnixTimeSeconds()}",
            CrossRevisionId = entitlementUuid,
            Description = book.Description,
            DownloadUrls =
            [
                new KoboStoreDownloadUrl
                {
                    Format = format,
                    Size = book.TotalFileSize,
                    Url = downloadUrl
                }
            ],
            EntitlementId = entitlementUuid,
            Language = book.Language,
            PublicationDate = book.PublicationDate is not null
                ? $"{book.PublicationDate}T00:00:00Z"
                : ToKoboTimestamp(book.CreatedAt),
            Publisher = new KoboStorePublisher { Name = book.Publisher },
            RevisionId = book.EntitlementUuid.ToString(),
            Title = book.Title,
            WorkId = entitlementUuid,
            ExternalIds = book.Identifiers.Select(id => new KoboStoreExternalId { Id = id }).ToArray(),
            ContributorRoles = BuildContributorRoles(book),
            Contributors = book.Authors.Select(a => a.Name).Concat(book.Contributors.Select(c => c.Name)).Distinct().ToList()
        };

        if (book.Series is not null)
        {
            metadata.Series = new KoboStoreSeriesInfo
            {
                Name = book.Series.Name,
                Number = (book.Series.Index ?? 1m).ToString("G29"),
                NumberFloat = (float)(book.Series.Index ?? 1m),
                Id = book.Series.SeriesUuid.ToString()
            };
        }

        if (relatedGroupId is not null)
            metadata.RelatedGroupId = relatedGroupId.Value.ToString();

        if (book.MediaType == MediaType.Audiobook)
        {
            metadata.Duration = book.Duration;
            metadata.IsAbridged = book.IsAbridged;
            metadata.Locale = new KoboStoreLocale { LanguageCode = book.Language ?? "en" };
        }

        return metadata;
    }

    /// <summary>
    /// Wraps entitlement + metadata into the polymorphic sync structure. Kobo uses separate JSON
    /// properties for ebook (BookEntitlement/BookMetadata) vs audiobook (AudiobookEntitlement/AudiobookMetadata).
    /// Using the wrong property type causes the device to silently ignore the item during sync.
    /// </summary>
    public static KoboStoreEntitlement ToStoreEntitlement(Book book, string baseUrl, KoboStoreReadingState? readingState = null, Guid? relatedGroupId = null)
    {
        var entitlement = ToEntitlement(book);
        var metadata = ToMetadata(book, baseUrl, relatedGroupId);

        return book.MediaType switch
        {
            MediaType.Audiobook => new KoboStoreEntitlement { AudiobookEntitlement = entitlement, AudiobookMetadata = metadata, ReadingState = readingState },
            _ => new KoboStoreEntitlement { BookEntitlement = entitlement, BookMetadata = metadata, ReadingState = readingState }
        };
    }

    public static KoboStoreEntitlement WithRemoved(KoboStoreEntitlement entitlement)
    {
        if (entitlement.AudiobookEntitlement is not null)
            return entitlement with { AudiobookEntitlement = entitlement.AudiobookEntitlement with { IsRemoved = true } };
        return entitlement with { BookEntitlement = entitlement.BookEntitlement! with { IsRemoved = true } };
    }

    /// <summary>
    /// Creates an audiobook manifest mimicking Kobo's DRM-free format. Empty Drm object with
    /// DrmType="" signals no DRM. Navigation provides the table of contents; Spine provides
    /// the audio stream URLs with per-chapter download endpoints. Both sections are required
    /// even though they contain largely redundant information.
    /// </summary>
    public static KoboStoreAudiobookManifest ToManifest(Book book, string baseUrl)
    {
        var entitlementUuid = book.EntitlementUuid.ToString();
        var chapters = book.Chapters.OrderBy(c => c.ChapterIndex).ToList();

        return new KoboStoreAudiobookManifest
        {
            Drm = new KoboStoreAudiobookDrm(),
            Navigation = chapters.Select(c => new KoboStoreAudiobookNavigation
            {
                Offset = 0.0,
                PartId = c.ChapterIndex,
                Title = c.Title
            }).ToList(),
            Spine = chapters.Select(c => new KoboStoreAudiobookSpineEntry
            {
                Bitrate = 0.0,
                Duration = c.Duration,
                Id = c.ChapterIndex,
                Url = $"{baseUrl}/audiobook/{entitlementUuid}/chapter/{c.ChapterIndex}"
            }).ToList()
        };
    }

    public static KoboStoreProductRecommendationItem ToProductRecommendationItem(Book book) => book.MediaType == MediaType.Audiobook
        ? new KoboStoreProductRecommendationItem { Audiobook = ToAudiobookProduct(book) }
        : new KoboStoreProductRecommendationItem { Book = ToEbookProduct(book) };

    public static KoboStoreProductRecommendationAudiobook ToAudiobookProduct(Book book) => new()
    {
        Id = book.EntitlementUuid.ToString(),
        Title = book.Title,
        Contributors = string.Join(",", book.Authors.Select(a => a.Name)),
        ContributorRoles = book.Authors.Select(a => new KoboStoreContributorRole { Name = a.Name }).ToList(),
        Description = book.Description,
        Language = book.Language,
        Duration = book.Duration ?? 0,
        TotalBytes = book.TotalFileSize,
        IsAbridged = book.IsAbridged ?? false,
        ISBN = book.Isbn,
        PublicationDate = DateTimeOffset.TryParse(book.PublicationDate, out var pubDate) ? pubDate.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ") : book.PublicationDate,
        Subtitle = book.Subtitle,
        PublisherName = book.Publisher,
        ImageId = $"{book.EntitlementUuid}--v{book.ModifiedAt.ToUnixTimeSeconds()}",
        SeriesName = book.Series?.Name,
        SeriesNumber = book.Series?.Index is not null ? ((int)book.Series.Index.Value).ToString() : null,
        SeriesId = book.Series?.SeriesUuid.ToString(),
        SeriesNumberFloat = book.Series?.Index is not null ? (float)book.Series.Index.Value : null
    };

    public static KoboStoreEbookProduct ToEbookProduct(Book book) => new()
    {
        Id = book.EntitlementUuid.ToString(),
        Title = book.Title,
        Contributors = string.Join(",", book.Authors.Select(a => a.Name).Concat(book.Contributors.Select(c => c.Name)).Distinct()),
        ContributorRoles = BuildContributorRoles(book),
        Description = book.Description,
        Language = book.Language,
        Locale = new KoboStoreLocale { LanguageCode = book.Language },
        ISBN = book.Isbn,
        PublicationDate = DateTimeOffset.TryParse(book.PublicationDate, out var pubDate) ? pubDate.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ") : book.PublicationDate,
        Subtitle = book.Subtitle,
        PublisherName = book.Publisher,
        ImageId = $"{book.EntitlementUuid}--v{book.ModifiedAt.ToUnixTimeSeconds()}",
        SeriesName = book.Series?.Name,
        SeriesNumber = book.Series?.Index is not null ? ((int)book.Series.Index.Value).ToString() : null,
        SeriesId = book.Series?.SeriesUuid.ToString(),
        SeriesNumberFloat = book.Series?.Index is not null ? (float)book.Series.Index.Value : null,
        Stats = book.PageCount is not null || book.WordCount is not null ? new KoboStoreEbookStats { Pages = book.PageCount ?? 0, WordCount = book.WordCount ?? 0 } : null
    };

    private static List<KoboStoreContributorRole> BuildContributorRoles(Book book)
    {
        var roles = book.Authors.Select(a => new KoboStoreContributorRole { Name = a.Name, Role = "Author" }).ToList();
        roles.AddRange(book.Contributors.Select(contributor => new KoboStoreContributorRole { Name = contributor.Name, Role = contributor.Role }));
        return roles;
    }
}
