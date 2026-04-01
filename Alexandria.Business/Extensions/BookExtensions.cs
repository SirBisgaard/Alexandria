using Alexandria.Business.Models;
using Alexandria.Business.Models.KoboStore;

namespace Alexandria.Business.Extensions;

public static class BookExtensions
{
    private static string ToKoboTimestamp(DateTime dateTime) => dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    extension(Book book)
    {
        public static Book FromMetadata(EpubMetadata metadata, string fileHash, string sourcePath, DateTime createdAt, DateTime lastModified)
        {
            return new Book
            {
                Uuid = metadata.Uuid,
                FileHash = fileHash,
                SourcePath = sourcePath,
                Title = metadata.Title,
                Authors = metadata.Authors,
                Language = metadata.Language,
                PublicationDate = metadata.PublicationDate,
                Description = metadata.Description,
                Publisher = metadata.Publisher,
                SeriesName = metadata.SeriesName,
                SeriesIndex = metadata.SeriesIndex,
                CoverFilePath = metadata.CoverFilePath,
                CreatedAt = createdAt,
                LastModified = lastModified
            };
        }
        
        public BookEntitlement ToEntitlement() => new()
        {
            ActivePeriod = new ActivePeriod { From = ToKoboTimestamp(book.CreatedAt) },
            Created = ToKoboTimestamp(book.CreatedAt),
            CrossRevisionId = book.Uuid.ToString(),
            Id = book.Uuid.ToString(),
            IsRemoved = false,
            LastModified = ToKoboTimestamp(book.LastModified),
            RevisionId = book.Uuid.ToString()
        };

        public BookMetadata ToMetadata(string baseUrl)
        {
            var uuid = book.Uuid.ToString();

            var metadata = new BookMetadata
            {
                CoverImageId = uuid,
                CrossRevisionId = uuid,
                Description = book.Description,
                DownloadUrls =
                [
                    new DownloadUrl
                    {
                        Format = "KEPUB",
                        Size = book.KepubFileSize,
                        Url = $"{baseUrl}/api/kobo/download/{uuid}/kepub"
                    }
                ],
                EntitlementId = uuid,
                Language = book.Language,
                PublicationDate = book.PublicationDate is not null
                    ? $"{book.PublicationDate}T00:00:00Z"
                    : ToKoboTimestamp(book.CreatedAt),
                Publisher = new Publisher { Name = book.Publisher },
                RevisionId = uuid,
                Title = book.Title,
                WorkId = uuid,
                ContributorRoles = book.Authors.Select(a => new ContributorRole { Name = a }).ToList(),
                Contributors = book.Authors
            };

            if (book.SeriesName is not null)
            {
                var seriesIndex = book.SeriesIndex ?? 1;
                metadata.Series = new SeriesInfo
                {
                    Name = book.SeriesName,
                    Number = seriesIndex,
                    NumberFloat = seriesIndex,
                    Id = Guid.GenerateUuidV5(book.SeriesName).ToString()
                };
            }

            return metadata;
        }
    }
}