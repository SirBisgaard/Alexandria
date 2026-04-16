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
            metadata.File.SourcePath = filePath;
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
        var validCoverPaths = currentBooks.Where(b => b.File.CoverFilePath is not null).Select(b => b.File.CoverFilePath!).ToHashSet();

        if (previousBooks is not null)
        {
            foreach (var existing in previousBooks)
            {
                if (!currentUuids.Contains(existing.BookUuid) && (existing.File.CoverFilePath is null || !validCoverPaths.Contains(existing.File.CoverFilePath)))
                    GetIndexerFor(existing.File.SourcePath).DeleteDerivedFiles(existing);
            }
        }

        foreach (var indexer in indexers)
            indexer.CleanupOrphanedFiles(currentUuids, validCoverPaths);
    }

    public async Task<Book> GetBook(Book book)
    {
        var indexer = GetIndexerFor(book.File.SourcePath);
        var freshBook = indexer.ExtractMetadata(book.File.SourcePath);
        freshBook.BookUuid = book.BookUuid;
        freshBook.File.SourcePath = book.File.SourcePath;
        freshBook.File.CoverFilePath = indexer.SaveCover(indexer.ExtractCoverBytes(book.File.SourcePath), book.BookUuid);
        await indexer.CreateDerivativeFiles(freshBook, book.File.SourcePath);
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
        var handler = GetHandlerFor(book.File.SourcePath);
        var indexer = GetIndexerFor(book.File.SourcePath);
        var modified = false;

        if (!handler.MetadataEquals(book, book.File.SourcePath))
        {
            handler.EmbedMetadata(book.File.SourcePath, book);
            modified = true;
        }

        if (coverUpdate is not null)
        {
            if (coverUpdate.RemoveCover)
                handler.RemoveCover(book.File.SourcePath);
            else if (coverUpdate.CoverData is not null)
                handler.EmbedCover(book.File.SourcePath, coverUpdate.CoverData, coverUpdate.MediaType!);
            modified = true;
        }

        if (modified)
            indexer.DeleteDerivedFiles(book);

        return await GetBook(book);
    }

    public Task CreateDerivativeFiles(Book book)
    {
        var indexer = GetIndexerFor(book.File.SourcePath);
        return indexer.CreateDerivativeFiles(book, book.File.SourcePath);
    }

    public Task<byte[]?> GetContentFile(Book book) => GetHandlerFor(book.File.SourcePath).ReadContent(book);

    public Task StreamChapter(Book book, int chapterIndex, Stream outputStream, CancellationToken cancellationToken) => GetHandlerFor(book.File.SourcePath).StreamChapter(book, chapterIndex, outputStream, cancellationToken);

    public Task<byte[]?> GetCoverFile(Book book, int? width = null, int? height = null, int? quality = null) => GetHandlerFor(book.File.SourcePath).ReadCover(book, width, height, quality);

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
