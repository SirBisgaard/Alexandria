namespace Alexandria.Business.Library.Models;

/// <summary>
/// A single chapter within an M4B audiobook, extracted from the file's embedded chapter markers via ffprobe.
/// All time values are in seconds (fractional).
/// </summary>
public class AudiobookChapter
{
    /// <summary>Zero-based position in the chapter list. Maps to the spine PartId in the Kobo manifest.</summary>
    public int ChapterIndex { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Chapter length in seconds (EndTime - StartTime).</summary>
    public double Duration { get; set; }

    /// <summary>Absolute start offset in seconds from the beginning of the audiobook.</summary>
    public double StartTime { get; set; }

    /// <summary>Absolute end offset in seconds from the beginning of the audiobook.</summary>
    public double EndTime { get; set; }

    /// <summary>Source filename for MP3 folder audiobooks. Null for M4B (single-file) audiobooks.</summary>
    public string? FileName { get; set; }
}
