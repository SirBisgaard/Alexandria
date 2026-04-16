namespace Alexandria.Business.Library.Models;

/// <summary>
/// Content format of the source file. Determines file handler dispatch,
/// Kobo sync entity type, and player routing on the device.
/// </summary>
public enum MediaType
{
    /// <summary>EPUB ebook, converted to KEPUB for Kobo delivery.</summary>
    Ebook,

    /// <summary>M4B audiobook, delivered as streamed chapters via manifest.</summary>
    Audiobook
}
