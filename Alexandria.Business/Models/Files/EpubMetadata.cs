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
    public double? SeriesIndex { get; set; }
    public string? CoverFilePath { get; set; }
}
