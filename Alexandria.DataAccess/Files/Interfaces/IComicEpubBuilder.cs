using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IComicEpubBuilder
{
    /// <summary>
    /// Renders <paramref name="book"/>'s CBZ into a fixed-layout EPUB sized for <paramref name="profile"/>
    /// using the <paramref name="rendering"/> tuning values and writes it to <paramref name="outputPath"/>. <paramref name="reportProgress"/> is called
    /// with (pages done, total pages) as each source page finishes.
    /// </summary>
    void Build(Book book, ComicDeviceProfile profile, ComicRenderingSettings rendering, string outputPath, Action<int, int>? reportProgress = null, CancellationToken cancellationToken = default);
}
