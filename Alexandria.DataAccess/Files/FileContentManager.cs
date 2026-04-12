using Alexandria.Business.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.DataAccess.Files.Interfaces;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Files;

public class FileContentManager(IEnumerable<IFileContentIndexer> indexers, IEnumerable<IFileContentMediaHandler> handlers, ILogger<FileContentManager> logger) : IFileContentManager
{
    public int GetBookFilesCount()
    {
        return indexers.SelectMany(i => i.GetContentFiles()).Count();
    }

    public async IAsyncEnumerable<(Book Metadata, byte[]? CoverBytes)> GetAllBooks()
    {
        var allFiles = indexers.SelectMany(i => i.GetContentFiles()).ToArray();
        logger.LogInformation("Indexing {Count} files from {IndexerCount} indexer(s)", allFiles.Length, indexers.Count());

        foreach (var filePath in allFiles)
        {
            var indexer = GetIndexerFor(filePath);
            var metadata = indexer.ExtractMetadata(filePath);
            metadata.SourcePath = filePath;
            var coverBytes = indexer.ExtractCoverBytes(filePath);
            yield return (metadata, coverBytes);
        }
    }

    public string? SaveCover(string filePath, byte[]? coverBytes, Guid bookUuid)
    {
        return GetIndexerFor(filePath).SaveCover(coverBytes, bookUuid);
    }

    public void CleanupStaleDerivatives(List<Book> currentBooks, List<Book>? previousBooks)
    {
        var currentUuids = currentBooks.Select(b => b.BookUuid).ToHashSet();
        var validCoverPaths = currentBooks.Where(b => b.CoverFilePath is not null).Select(b => b.CoverFilePath!).ToHashSet();

        if (previousBooks is not null)
        {
            foreach (var existing in previousBooks)
            {
                if (!currentUuids.Contains(existing.BookUuid) && (existing.CoverFilePath is null || !validCoverPaths.Contains(existing.CoverFilePath)))
                    GetIndexerFor(existing.SourcePath).DeleteDerivedFiles(existing);
            }
        }

        foreach (var indexer in indexers)
            indexer.CleanupOrphanedFiles(currentUuids, validCoverPaths);
    }

    public async Task<Book> GetBook(Book book)
    {
        var indexer = GetIndexerFor(book.SourcePath);
        var freshBook = indexer.ExtractMetadata(book.SourcePath);
        freshBook.BookUuid = book.BookUuid;
        freshBook.SourcePath = book.SourcePath;
        freshBook.CoverFilePath = indexer.SaveCover(indexer.ExtractCoverBytes(book.SourcePath), book.BookUuid);
        await indexer.CreateDerivativeFiles(freshBook, book.SourcePath);
        return freshBook;
    }

    /// <summary>
    /// Updates a book's source file with new metadata and/or cover. Compares metadata before
    /// embedding to avoid unnecessary file writes — each write triggers file timestamp change,
    /// kepub reconversion, and device re-sync. Extension-based dispatch routes to the correct
    /// IFileContentMediaHandler implementation (one per media type, matched by SupportedExtensions).
    /// </summary>
    public async Task<Book> UpdateBook(Book book, CoverUpdate? coverUpdate = null)
    {
        var handler = GetHandlerFor(book.SourcePath);
        var indexer = GetIndexerFor(book.SourcePath);
        var modified = false;

        if (!handler.MetadataEquals(book, book.SourcePath))
        {
            handler.EmbedMetadata(book.SourcePath, book);
            modified = true;
        }

        if (coverUpdate is not null)
        {
            if (coverUpdate.RemoveCover)
                handler.RemoveCover(book.SourcePath);
            else if (coverUpdate.CoverData is not null)
                handler.EmbedCover(book.SourcePath, coverUpdate.CoverData, coverUpdate.MediaType!);
            modified = true;
        }

        if (modified)
            indexer.DeleteDerivedFiles(book);

        return await GetBook(book);
    }

    public Task CreateDerivativeFiles(Book book)
    {
        var indexer = GetIndexerFor(book.SourcePath);
        return indexer.CreateDerivativeFiles(book, book.SourcePath);
    }

    public Task<byte[]?> GetContentFile(Book book) => GetHandlerFor(book.SourcePath).ReadContent(book);

    public Task<byte[]?> GetChapterFile(Book book, int chapterIndex) => GetHandlerFor(book.SourcePath).ReadChapter(book, chapterIndex);

    public Task<byte[]?> GetCoverFile(Book book, int? width = null, int? height = null, int? quality = null) => GetHandlerFor(book.SourcePath).ReadCover(book, width, height, quality);

    private IFileContentIndexer GetIndexerFor(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return indexers.FirstOrDefault(i => i.SupportedExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)))
            ?? throw new NotSupportedException($"No indexer registered for extension '{extension}'.");
    }

    private IFileContentMediaHandler GetHandlerFor(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return handlers.FirstOrDefault(h => h.SupportedExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)))
            ?? throw new NotSupportedException($"No media handler registered for extension '{extension}'.");
    }
}
