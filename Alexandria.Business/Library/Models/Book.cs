namespace Alexandria.Business.Library.Models;

/// <summary>
/// Editorial metadata for a library book. Identity and content description only —
/// file-specific properties (paths, sizes, codec) live on <see cref="BookFile"/>.
/// </summary>
public class Book
{
    /// <summary>Deterministic UUID v5 derived from "title:authors". Changes if title or authors are edited.</summary>
    public Guid BookUuid { get; set; }

    /// <summary>Stable UUID used by Kobo devices for sync. Preserved across reindex even when BookUuid changes.</summary>
    public Guid EntitlementUuid { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Language { get; set; } = string.Empty;
    public string? Isbn { get; set; }

    /// <summary>Publication date as extracted from metadata. Free-form string (e.g. "2024", "2024-03-15").</summary>
    public string? PublicationDate { get; set; }

    public string? Description { get; set; }
    public string? Publisher { get; set; }

    /// <summary>Dublin Core identifiers (ISBN, URI, etc.) extracted from the source file.</summary>
    public List<string> Identifiers { get; set; } = [];

    /// <summary>Genre/subject tags extracted from the source file.</summary>
    public List<string> Subjects { get; set; } = [];

    /// <summary>Dublin Core rights statement (e.g. copyright notice).</summary>
    public string? Rights { get; set; }

    /// <summary>Dublin Core source — the resource from which this book is derived.</summary>
    public string? Source { get; set; }

    /// <summary>Dublin Core type — the nature or genre of the resource.</summary>
    public string? Type { get; set; }

    /// <summary>Dublin Core relation — a related resource.</summary>
    public string? Relation { get; set; }

    /// <summary>Dublin Core coverage — spatial or temporal scope of the content.</summary>
    public string? Coverage { get; set; }

    /// <summary>Dublin Core format — the file format or medium.</summary>
    public string? Format { get; set; }

    /// <summary>Timestamp when the book was first indexed. Preserved across reindex.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Timestamp of the last metadata or file change.</summary>
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>Soft-delete flag. Archived books are excluded from sync and recommendations.</summary>
    public bool IsArchived { get; set; }

    /// <summary>File-specific properties: paths, sizes, duration, codec. Always populated via JOIN.</summary>
    public BookFile File { get; set; } = new();

    /// <summary>Series membership with optional position index. Null if the book is standalone.</summary>
    public Series? Series { get; set; }

    /// <summary>Kobo reading state (progress, status). Null if the device hasn't reported any state.</summary>
    public ReadingProgress? ReadingProgress { get; set; }

    public List<Author> Authors { get; set; } = [];

    /// <summary>Audiobook chapters extracted from the audiobook files. Empty for ebooks.</summary>
    public List<AudiobookChapter> Chapters { get; set; } = [];

    /// <summary>Non-author contributors (e.g. narrators). Role is free-form but audiobooks only support "Narrator".</summary>
    public List<Contributor> Contributors { get; set; } = [];
}
