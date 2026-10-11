using Alexandria.Business.Library.Models;

namespace Alexandria.Business.Interfaces;

/// <summary>
/// Locates the fixed-layout EPUB renditions built for each comic and device profile. A rendition's
/// file name carries a signature of everything it was built from (source file, metadata, reading
/// direction, colour mode, rendering settings), so any change makes the old file unreachable and a
/// new one is built on the next library sync.
/// </summary>
public interface IComicRenditionStore
{
    /// <summary>The path the current rendition of <paramref name="book"/> for <paramref name="profile"/> lives at, whether or not it has been built.</summary>
    Task<string> GetRenditionPath(Book book, ComicDeviceProfile profile);

    /// <summary>The current rendition if it has been built, otherwise null.</summary>
    Task<FileInfo?> FindRendition(Book book, ComicDeviceProfile profile);

    /// <summary>Distinct profiles of all registered devices whose model is known.</summary>
    Task<List<ComicDeviceProfile>> GetActiveProfiles();

    /// <summary>Removes every rendition of the book across all profiles.</summary>
    void DeleteRenditions(Guid bookUuid);
}
