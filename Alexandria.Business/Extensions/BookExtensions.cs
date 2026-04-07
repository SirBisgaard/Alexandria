using Alexandria.Business.Models;
using Alexandria.Business.Models.Files;
using Alexandria.Business.Models.KoboStore;

namespace Alexandria.Business.Extensions;

public static class BookExtensions
{
    private static string ToKoboTimestamp(DateTimeOffset dateTime) => dateTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    extension(Book book)
    {
        public static Book FromMetadata(EpubMetadata metadata, string fileHash, string sourcePath, DateTimeOffset createdAt, DateTimeOffset lastModified)
        {
            var bookUuid = metadata.AlexandriaUuid ?? metadata.DeterministicUuid;
            var entitlementUuid = bookUuid; // Can be changed later on in the frontend.
            var coverImageUuid = Guid.GenerateUuidV5("cover:" + fileHash);

            return new Book
            {
                BookUuid = bookUuid,
                EntitlementUuid = entitlementUuid,
                CoverImageUuid = coverImageUuid,
                FileHash = fileHash,
                SourcePath = sourcePath,
                Title = metadata.Title,
                Language = metadata.Language,
                PublicationDate = metadata.PublicationDate,
                Description = metadata.Description,
                Publisher = metadata.Publisher,
                CoverFilePath = metadata.CoverFilePath,
                CreatedAt = createdAt,
                LastModified = lastModified
            };
        }

        public EpubMetadata ToEpubMetadata() => new()
        {
            AlexandriaUuid = book.BookUuid,
            Title = book.Title,
            Authors = book.Authors.Select(a => a.Name).ToList(),
            Language = book.Language,
            PublicationDate = book.PublicationDate,
            Description = book.Description,
            Publisher = book.Publisher,
            SeriesName = book.Series?.Name,
            SeriesIndex = book.Series?.Index
        };

        public BookEntitlement ToEntitlement() => new()
        {
            ActivePeriod = new ActivePeriod { From = ToKoboTimestamp(book.CreatedAt) },
            Created = ToKoboTimestamp(book.CreatedAt),
            CrossRevisionId = book.EntitlementUuid.ToString(),
            Id = book.EntitlementUuid.ToString(),
            IsRemoved = false,
            LastModified = ToKoboTimestamp(book.LastModified),
            RevisionId = book.EntitlementUuid.ToString()
        };

        public BookMetadata ToMetadata(string baseUrl)
        {
            var entitlementUuid = book.EntitlementUuid.ToString();

            var metadata = new BookMetadata
            {
                CoverImageId = book.CoverImageUuid?.ToString() ?? entitlementUuid,
                CrossRevisionId = entitlementUuid,
                Description = book.Description,
                DownloadUrls =
                [
                    new DownloadUrl
                    {
                        Format = "KEPUB",
                        Size = book.KepubFileSize,
                        Url = $"{baseUrl}/download/{entitlementUuid}/kepub"
                    }
                ],
                EntitlementId = entitlementUuid,
                Language = book.Language,
                PublicationDate = book.PublicationDate is not null
                    ? $"{book.PublicationDate}T00:00:00Z"
                    : ToKoboTimestamp(book.CreatedAt),
                Publisher = new Publisher { Name = book.Publisher },
                RevisionId = book.EntitlementUuid.ToString(),
                Title = book.Title,
                WorkId = entitlementUuid,
                ContributorRoles = book.Authors.Select(a => new ContributorRole { Name = a.Name }).ToList(),
                Contributors = book.Authors.Select(a => a.Name).ToList()
            };

            if (book.Series is not null)
            {
                var seriesIndex = (int)(book.Series.Index ?? 1m);
                metadata.Series = new SeriesInfo
                {
                    Name = book.Series.Name,
                    Number = seriesIndex,
                    NumberFloat = (float)(book.Series.Index ?? 1m),
                    Id = book.Series.SeriesUuid.ToString()
                };
            }

            return metadata;
        }
    }
}
