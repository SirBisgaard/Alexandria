namespace Alexandria.Business.Library.Models;

/// <summary>
/// File-specific properties derived from the physical source file (EPUB or M4B).
/// Stored in the BookFiles table, 1:1 with Books. Recalculated on every reindex.
/// </summary>
public class BookFile
{
    public Guid BookUuid { get; set; }

    /// <summary>Whether this is an ebook (EPUB) or audiobook (M4B).</summary>
    public MediaType MediaType { get; set; } = MediaType.Ebook;

    /// <summary>Absolute filesystem path to the source file.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Source file name without directory (computed from SourcePath).</summary>
    public string FileName => Path.GetFileName(SourcePath);

    /// <summary>Absolute path to the extracted/saved cover image. Null if no cover.</summary>
    public string? CoverFilePath { get; set; }

    /// <summary>Absolute path to the KEPUB derivative. Ebooks only; null for audiobooks.</summary>
    public string? KepubFilePath { get; set; }

    /// <summary>File size in bytes. For ebooks this is the KEPUB derivative size; for audiobooks, the M4B source size.</summary>
    public long TotalFileSize { get; set; }

    /// <summary>Estimated page count. Computed from WordCount / WordsPerPage setting. Ebooks only.</summary>
    public int? PageCount { get; set; }

    /// <summary>Total word count extracted from EPUB content. Null for audiobooks.</summary>
    public int? WordCount { get; set; }

    /// <summary>Total audiobook duration in seconds. Null for ebooks.</summary>
    public int? Duration { get; set; }

    /// <summary>Whether the audiobook is an abridged edition. Null for ebooks.</summary>
    public bool? IsAbridged { get; set; }

    /// <summary>Audio codec name as reported by ffprobe (e.g. "aac", "mp3"). Null for ebooks.</summary>
    public string? AudioCodec { get; set; }

    /// <summary>Audio bitrate in bits per second as reported by ffprobe. Null for ebooks.</summary>
    public int? AudioBitrate { get; set; }
}
