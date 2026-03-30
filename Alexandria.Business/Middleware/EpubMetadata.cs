namespace Alexandria.Business.Middleware;

public class EpubMetadata
{
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