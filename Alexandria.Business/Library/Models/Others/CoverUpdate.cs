namespace Alexandria.Business.Library.Models.Others;

/// <summary>
/// Describes a cover image change. Either sets new cover data or removes the existing one.
/// </summary>
/// <param name="CoverData">Raw image bytes for the new cover. Null when removing.</param>
/// <param name="MediaType">MIME type of the cover image (e.g. "image/jpeg"). Required when setting.</param>
/// <param name="RemoveCover">When true, the existing cover is stripped from the source file.</param>
public record CoverUpdate(byte[]? CoverData = null, string? MediaType = null, bool RemoveCover = false) { }
