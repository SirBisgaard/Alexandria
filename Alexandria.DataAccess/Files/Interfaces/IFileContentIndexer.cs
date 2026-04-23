using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IFileContentIndexer
{
    /// <summary>
    /// Returns true if this indexer handles the given path. File-based indexers check
    /// the file extension; folder-based indexers inspect directory layout.
    /// </summary>
    bool CanHandle(string path);
    string[] GetContentFiles();
    Task<(Book Metadata, byte[]? CoverBytes)> Extract(string filePath);
    string? SaveCover(byte[]? coverBytes, Guid bookUuid);
    Task CreateDerivativeFiles(Book book, string sourcePath);
    void DeleteDerivedFiles(Book book);
    void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths);
}
