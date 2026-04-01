namespace Alexandria.Business.Models;

public class Book
{
    public Guid Uuid { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? SeriesName { get; set; }
    public int? SeriesIndex { get; set; }
    public string? CoverFilePath { get; set; }
    public string? KepubFilePath { get; set; }
    public long KepubFileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsArchived { get; set; }

    public ReadingProgress? ReadingProgress { get; set; }
}
