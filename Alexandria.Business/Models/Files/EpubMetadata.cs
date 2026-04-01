using Alexandria.Business.Extensions;

namespace Alexandria.Business.Models.Files;

public class EpubMetadata
{

    public Guid Uuid => Guid.GenerateUuidV5($"{Title.Trim().ToLowerInvariant()}:{string.Join(",", Authors.Select(a => a.Trim().ToLowerInvariant()))}");
    public string Title { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? SeriesName { get; set; }
    public int? SeriesIndex { get; set; }
    public bool CoverExtracted { get; set; }
    public string? CoverFilePath { get; set; }
}