namespace Alexandria.Business.Models;

public class Book
{
    public Guid BookUuid { get; set; }
    public Guid EntitlementUuid { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(SourcePath);
    public string Title { get; set; } = string.Empty;
    public List<Author> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public List<string> Identifiers { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
    public List<string> Contributors { get; set; } = [];
    public string? Rights { get; set; }
    public string? Source { get; set; }
    public string? Type { get; set; }
    public string? Relation { get; set; }
    public string? Coverage { get; set; }
    public string? Format { get; set; }
    public Series? Series { get; set; }
    public string? CoverFilePath { get; set; }
    public string? KepubFilePath { get; set; }
    public long KepubFileSize { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public Guid? CoverImageUuid { get; set; }
    public bool IsArchived { get; set; }

    public ReadingProgress? ReadingProgress { get; set; }
}
