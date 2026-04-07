using Alexandria.Business.Extensions;

namespace Alexandria.Business.Models.Files;

public class EpubMetadata
{
    public Guid? AlexandriaUuid { get; set; }
    public Guid DeterministicUuid
    {
        get
        {
            var title = Title.ToLowerInvariant().Trim();
            var authors = string.Join(',', Authors.Select(a => a.ToLowerInvariant().Trim()));
            var series = SeriesName?.ToLowerInvariant().Trim() + "#" + SeriesIndex;

            return Guid.GenerateUuidV5($"{title}:{authors}:{series}");
        }
    }

    public string Title { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? SeriesName { get; set; }
    public decimal? SeriesIndex { get; set; }
    public string? CoverFilePath { get; set; }

    public bool MetadataEquals(EpubMetadata other)
    {
        return Title == other.Title
            && Language == other.Language
            && Normalize(PublicationDate) == Normalize(other.PublicationDate)
            && Normalize(Description) == Normalize(other.Description)
            && Normalize(Publisher) == Normalize(other.Publisher)
            && Normalize(SeriesName) == Normalize(other.SeriesName)
            && SeriesIndex == other.SeriesIndex
            && Authors.SequenceEqual(other.Authors, StringComparer.Ordinal);
    }

    private static string? Normalize(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
