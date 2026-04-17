using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IFileContentIndexer
{
    string[] SupportedExtensions { get; }
    string[] GetContentFiles();
    Task<(Book Metadata, byte[]? CoverBytes)> Extract(string filePath);
    string? SaveCover(byte[]? coverBytes, Guid bookUuid);
    Task CreateDerivativeFiles(Book book, string sourcePath);
    void DeleteDerivedFiles(Book book);
    void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths);
}
