using Alexandria.Business.Library.Extensions;
using Alexandria.Business.Library.Interfaces;
using Alexandria.Business.Library.Models;
using Alexandria.Business.Library.Models.Others;
using Alexandria.DataAccess.Files.Interfaces;
using Microsoft.Extensions.Logging;

namespace Alexandria.DataAccess.Files;

public class FileContentManager(IEnumerable<IFileContentIndexer> indexers, IEnumerable<IFileContentMediaHandler> handlers, ILogger<FileContentManager> logger) : IFileContentManager
{
    public async Task<List<Book>> GetAllBooks(List<Book>? existingBooks = null, IProgress<(int current, int total)>? progress = null)
    {
        var books = new List<Book>();
        var allFiles = indexers.SelectMany(i => i.GetContentFiles()).ToArray();
        logger.LogInformation("Indexing {Count} files from {IndexerCount} indexer(s)", allFiles.Length, indexers.Count());

        for (var i = 0; i < allFiles.Length; i++)
        {
            progress?.Report((i + 1, allFiles.Length));
            var filePath = allFiles[i];
            var indexer = GetIndexerFor(filePath);
            var fileName = Path.GetFileName(filePath);

            logger.LogDebug("Indexing [{Index}/{Total}]: {FileName}", i + 1, allFiles.Length, fileName);

            var fileHash = await indexer.ComputeFileHash(filePath);
            var (book, coverFilePath) = indexer.ExtractAll(filePath);

            if (book.BookUuid == Guid.Empty)
            {
                book.BookUuid = book.GenerateDeterministicUuid();
                logger.LogInformation("Stamping UUID {Uuid} into {FileName}", book.BookUuid, fileName);
                indexer.StampIdentifier(filePath, book);
                fileHash = await indexer.ComputeFileHash(filePath);
            }

            book.FileHash = fileHash;
            book.SourcePath = filePath;
            book.CoverFilePath = coverFilePath;
            await indexer.CreateDerivativeFiles(book, filePath);

            logger.LogDebug("Indexed '{Title}' by {Authors} ({Uuid})", book.Title, string.Join(", ", book.Authors.Select(a => a.Name)), book.BookUuid);
            books.Add(book);
        }

        CleanupStaleDerivatives(books, existingBooks);
        logger.LogInformation("Indexing complete: {Count} books", books.Count);

        return books;
    }

    public async Task<Book> GetBook(Book book)
    {
        var indexer = GetIndexerFor(book.SourcePath);
        var (freshBook, coverFilePath) = indexer.ExtractAll(book.SourcePath);
        freshBook.SourcePath = book.SourcePath;
        freshBook.CoverFilePath = coverFilePath;
        await indexer.CreateDerivativeFiles(freshBook, book.SourcePath);
        freshBook.FileHash = await indexer.ComputeFileHash(book.SourcePath);
        return freshBook;
    }

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

    public Task<byte[]?> GetContentFile(Book book) => GetHandlerFor(book.SourcePath).ReadContent(book);

    public Task<byte[]?> GetChapterFile(Book book, int chapterIndex) => GetHandlerFor(book.SourcePath).ReadChapter(book, chapterIndex);

    public Task<byte[]?> GetCoverFile(Book book, int? width = null, int? height = null, int? quality = null) => GetHandlerFor(book.SourcePath).ReadCover(book, width, height, quality);

    private void CleanupStaleDerivatives(List<Book> currentBooks, List<Book>? existingBooks)
    {
        var currentUuids = currentBooks.Select(b => b.BookUuid).ToHashSet();
        var validCoverPaths = currentBooks.Where(b => b.CoverFilePath is not null).Select(b => b.CoverFilePath!).ToHashSet();

        if (existingBooks is not null)
        {
            foreach (var existing in existingBooks)
            {
                if (!currentUuids.Contains(existing.BookUuid))
                    GetIndexerFor(existing.SourcePath).DeleteDerivedFiles(existing);
            }
        }

        foreach (var indexer in indexers)
            indexer.CleanupOrphanedFiles(currentUuids, validCoverPaths);
    }

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
