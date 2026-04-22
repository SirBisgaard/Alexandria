namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// Describes a cover image change. Either sets new cover data or removes the existing one.
/// </summary>
public record CoverUpdate
{
    /// <summary>Raw image bytes for the new cover. Null when removing.</summary>
    public byte[]? CoverData { get; init; }

    /// <summary>MIME type of the cover image (e.g. "image/jpeg"). Required when setting.</summary>
    public string? MediaType { get; init; }

    /// <summary>When true, the existing cover is stripped from the source file.</summary>
    public bool RemoveCover { get; init; }
}
