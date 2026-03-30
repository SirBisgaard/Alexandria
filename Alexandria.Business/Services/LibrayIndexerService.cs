using Alexandria.Business.Interfaces;
using Alexandria.Business.Models;
using Alexandria.Business.Models.Settings;

namespace Alexandria.Business.Services;

public class LibraryIndexerService(AlexandriaSettings settings, IIndexFileService indexFileService, IEpubMetadataExstractor epubMetadataExstractor, IKepubifyConverter kepubifyConverter) : ILibraryIndexerService
{
    public async Task Index()
    {
        // Load index file.
        await indexFileService.LoadAsync();

        // Load book files in book folder.
        if (!Directory.Exists(settings.BooksFolder))
            return;

        var epubFiles = Directory.GetFiles(settings.BooksFolder, "*.epub");

        // Remove stale entries whose source epub no longer exists or whose hash changed.
        var currentHashes = new HashSet<string>();
        foreach (var epubPath in epubFiles)
            currentHashes.Add(await indexFileService.ComputeFileHashAsync(epubPath));

        var staleBooks = indexFileService.Books.Where(b => !currentHashes.Contains(b.FileHash)).ToList();
        foreach (var stale in staleBooks)
        {
            if (stale.CoverFilePath is not null && File.Exists(stale.CoverFilePath))
                File.Delete(stale.CoverFilePath);
            if (stale.KepubFilePath is not null && File.Exists(stale.KepubFilePath))
                File.Delete(stale.KepubFilePath);
            indexFileService.Books.Remove(stale);
        }

        foreach (var epubPath in epubFiles)
        {
            // Convert new or updated books. (SHA hash not matched)
            var fileHash = await indexFileService.ComputeFileHashAsync(epubPath);
            if (indexFileService.FindByHash(fileHash) is not null)
                continue;

            // Extract new or updated cover photos (SHA hash not matched)
            var metadata = epubMetadataExstractor.Extract(epubPath, settings.CoversFolder);

            // Convert to KEPUB.
            var kepubFileName = Path.GetFileNameWithoutExtension(epubPath) + ".kepub.epub";
            var kepubPath = Path.Combine(settings.KepubsFolder, kepubFileName);
            await kepubifyConverter.ConvertAsync(epubPath, kepubPath);

            var book = Book.FromMetadata(metadata, fileHash, epubPath);
            book.KepubFilePath = kepubPath;
            indexFileService.Books.Add(book);
        }

        // Update index file with changes.
        await indexFileService.SaveAsync();
    }
}
