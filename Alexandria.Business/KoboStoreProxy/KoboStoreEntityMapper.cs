using Alexandria.Business.KoboStoreProxy.Models.Store;
using Alexandria.Business.Library.Models;

namespace Alexandria.Business.KoboStoreProxy;

public static class KoboStoreEntityMapper
{
    private static string ToKoboTimestamp(DateTimeOffset dateTime) => dateTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

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
            CoverImageId = book.CoverImageUuid?.ToString() ?? entitlementUuid,
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

    private static List<KoboStoreContributorRole> BuildContributorRoles(Book book)
    {
        var roles = book.Authors.Select(a => new KoboStoreContributorRole { Name = a.Name, Role = "Author" }).ToList();

        foreach (var contributor in book.Contributors)
        {
            roles.Add(new KoboStoreContributorRole { Name = contributor.Name, Role = contributor.Role });
        }

        return roles;
    }
}
