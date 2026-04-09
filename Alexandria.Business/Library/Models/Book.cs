namespace Alexandria.Business.Library.Models;

public class Book
{
    public Guid BookUuid { get; set; }
    public Guid EntitlementUuid { get; set; }
    public MediaType MediaType { get; set; } = MediaType.Ebook;
    public string FileHash { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(SourcePath);
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public List<Author> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? Isbn { get; set; }
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public List<string> Identifiers { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
    public List<Contributor> Contributors { get; set; } = [];
    public string? Rights { get; set; }
    public string? Source { get; set; }
    public string? Type { get; set; }
    public string? Relation { get; set; }
    public string? Coverage { get; set; }
    public string? Format { get; set; }
    public Series? Series { get; set; }
    public string? CoverFilePath { get; set; }
    public string? KepubFilePath { get; set; }
    public long TotalFileSize { get; set; }
    public int? PageCount { get; set; }
    public int? WordCount { get; set; }
    public int? Duration { get; set; }
    public bool? IsAbridged { get; set; }
    public List<AudiobookChapter> Chapters { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public Guid? CoverImageUuid { get; set; }
    public bool IsArchived { get; set; }

    public ReadingProgress? ReadingProgress { get; set; }
}
