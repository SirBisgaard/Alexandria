using Alexandria.Business.Library.Models;

namespace Alexandria.DataAccess.Files.Interfaces;

public interface IFileContentIndexer
{
    string[] SupportedExtensions { get; }
    string[] GetContentFiles();
    Book ExtractMetadata(string filePath);
    byte[]? ExtractCoverBytes(string filePath);
    (Book Book, string? CoverFilePath) ExtractAll(string filePath, Guid bookUuid);
    Task CreateDerivativeFiles(Book book, string sourcePath);
    void DeleteDerivedFiles(Book book);
    void CleanupOrphanedFiles(HashSet<Guid> validBookUuids, HashSet<string> validCoverPaths);
}
