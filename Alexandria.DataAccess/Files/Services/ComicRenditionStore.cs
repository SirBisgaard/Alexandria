using System.Globalization;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Settings;
using Alexandria.DataAccess.Files.Books.Comics;

namespace Alexandria.DataAccess.Files.Services;

/// <summary>
/// Renditions live at <c>{ComicsFolder}/{profile.Key}/{bookUuid}.{signature}.epub</c>. The
/// signature hashes everything the output depends on: the source file's size and modification
/// time (metadata edits rewrite ComicInfo.xml, changing the file), the reading direction and colour
/// mode, <see cref="ComicEpubBuilder.BuilderVersion"/> and every current
/// <see cref="ComicRenderingSettings"/> value from the Settings page. Any change makes the old file
/// unreachable, so a stale rendition can never be served.
/// </summary>
public class ComicRenditionStore(AlexandriaSettings settings, ISettingsService settingsService, IKoboDeviceRepository deviceRepository) : IComicRenditionStore
{
    public async Task<string> GetRenditionPath(Book book, ComicDeviceProfile profile)
    {
        var rendering = (await settingsService.Get()).ComicRendering;
        return Path.Combine(settings.ComicsFolder, profile.Key, $"{book.BookUuid}.{ComputeSignature(book, rendering)}.epub");
    }

    public async Task<FileInfo?> FindRendition(Book book, ComicDeviceProfile profile)
    {
        var info = new FileInfo(await GetRenditionPath(book, profile));
        return info.Exists ? info : null;
    }

    public async Task<List<ComicDeviceProfile>> GetActiveProfiles()
    {
        var devices = await deviceRepository.GetAll();
        return devices
            .Select(d => ComicDeviceProfile.ForModelCode(d.ModelCode))
            .OfType<ComicDeviceProfile>()
            .Distinct()
            .ToList();
    }

    public void DeleteRenditions(Guid bookUuid)
    {
        if (!Directory.Exists(settings.ComicsFolder))
            return;
        foreach (var profileFolder in Directory.GetDirectories(settings.ComicsFolder))
        {
            foreach (var file in Directory.GetFiles(profileFolder, $"{bookUuid}.*"))
                File.Delete(file);
        }
    }

    private static string ComputeSignature(Book book, ComicRenderingSettings rendering)
    {
        var source = new FileInfo(book.File.SourcePath);
        var seed = string.Join('|',
            ComicEpubBuilder.BuilderVersion.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(rendering),
            source.Exists ? source.Length.ToString(CultureInfo.InvariantCulture) : "missing",
            source.Exists ? source.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "missing",
            book.File.ReadingDirection ?? ComicReadingDirection.LeftToRight,
            book.File.ColorMode ?? ComicColorMode.Auto);
        return Convert.ToHexStringLower(XxHash64.Hash(Encoding.UTF8.GetBytes(seed)));
    }
}
